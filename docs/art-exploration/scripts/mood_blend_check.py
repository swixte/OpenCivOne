"""Score candidate maps: sprites must each fall inside one cell of an 8x6 grid on the map."""

import sys
import numpy as np
from PIL import Image, ImageDraw

OUT = "/tmp/mood-blend"


def sidebar_x(arr):
    y = 0.2126 * arr[:, :, 0] + 0.7152 * arr[:, :, 1] + 0.0722 * arr[:, :, 2]
    dx = np.abs(np.diff(y, axis=1))
    strong = dx > 28
    w = arr.shape[1]
    best, best_score = int(w * 0.16), -1.0
    for x in range(int(w * 0.10), int(w * 0.30)):
        score = float(strong[:, x].mean())
        if score > best_score:
            best_score, best = score, x
    return best


def dilate(mask, r=4):
    h, w = mask.shape
    ys, xs = np.where(mask)
    out = np.zeros_like(mask)
    if len(xs) == 0:
        return out
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dy * dy + dx * dx > r * r:
                continue
            yy, xx = ys + dy, xs + dx
            ok = (yy >= 0) & (yy < h) & (xx >= 0) & (xx < w)
            out[yy[ok], xx[ok]] = True
    return out


def components(mask):
    h, w = mask.shape
    vis = np.zeros((h, w), dtype=np.uint8)
    # label via boolean scan
    ys, xs = np.where(mask)
    if len(xs) == 0:
        return []
    parent = {}

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    def union(a, b):
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[rb] = ra

    idx = -np.ones((h, w), dtype=np.int32)
    for i, (y, x) in enumerate(zip(ys.tolist(), xs.tolist())):
        idx[y, x] = i
        parent[i] = i
    for y, x, i in zip(ys.tolist(), xs.tolist(), range(len(xs))):
        if x + 1 < w and idx[y, x + 1] >= 0:
            union(i, int(idx[y, x + 1]))
        if y + 1 < h and idx[y + 1, x] >= 0:
            union(i, int(idx[y + 1, x]))
    groups = {}
    for i, (y, x) in enumerate(zip(ys.tolist(), xs.tolist())):
        r = find(i)
        groups.setdefault(r, []).append((y, x))
    comps = []
    for cells in groups.values():
        aa = np.array(cells)
        comps.append((len(cells), int(aa[:, 1].min()), int(aa[:, 0].min()), int(aa[:, 1].max()), int(aa[:, 0].max()), cells))
    comps.sort(key=lambda c: -c[0])
    return comps


def cell_span(x0, y0, x1, y1, sx, w, h):
    cw, ch = (w - sx) / 8.0, h / 6.0
    c0 = int(np.floor((x0 - sx) / cw))
    c1 = int(np.floor((x1 - sx) / cw))
    r0 = int(np.floor(y0 / ch))
    r1 = int(np.floor(y1 / ch))
    return c0, c1, r0, r1, cw, ch


def largest_fg(arr, x0, y0, x1, y1, thr):
    sub = arr[y0:y1, x0:x1]
    if sub.size == 0:
        return None
    med = np.median(sub.reshape(-1, 3), axis=0)
    dist = np.linalg.norm(sub - med, axis=2)
    mask = dist > thr
    # drop a 3px rim so frames don't dominate
    mask[:3, :] = False
    mask[-3:, :] = False
    mask[:, :3] = False
    mask[:, -3:] = False
    comps = components(mask)
    if not comps:
        return None
    count, a, b, c, d, cells = comps[0]
    # ignore tiny noise
    if count < 40:
        return None
    return (x0 + a, y0 + b, x0 + c, y0 + d, count)


def check(path):
    im = Image.open(path).convert("RGB")
    arr = np.asarray(im).astype(np.float32)
    h, w, _ = arr.shape
    sx = sidebar_x(arr)
    r, g, b = arr[:, :, 0], arr[:, :, 1], arr[:, :, 2]
    yel = (r > 225) & (g > 185) & (b < 80) & (r > g - 5) & ((r + g) > b + 260)
    yel[:, : sx + 6] = False
    # keep only a thin-rectangle-like set: columns/rows with few yellow pixels are noise
    ys, xs = np.where(yel)
    report = {"file": path, "sidebar": sx, "size": (w, h)}
    if len(xs) < 80:
        report["yellow"] = None
    else:
        # bounding box of the densest yellow cluster
        comps = components(dilate(yel, 2))
        # choose component whose bbox is most rectangle-like and not huge
        best = None
        for count, x0, y0, x1, y1, cells in comps:
            bw, bh = x1 - x0, y1 - y0
            if bw < 40 or bh < 40 or bw > 400 or bh > 400:
                continue
            if best is None or count > best[0]:
                best = (count, x0, y0, x1, y1)
        report["yellow"] = None if best is None else best[1:]
    roofs = (r > 145) & (g < 110) & (b < 120) & (r > g + 55) & (r > b + 40)
    roofs[:, : sx + 8] = False
    if report["yellow"]:
        x0, y0, x1, y1 = report["yellow"]
        roofs[max(0, y0 - 8) : y1 + 8, max(0, x0 - 8) : x1 + 8] = False
    roof_comps = components(dilate(roofs, 5))
    # filter to original roof pixels inside each dilated component by re-bbox of roofs in that window
    cities = []
    for count, x0, y0, x1, y1, cells in roof_comps[:6]:
        window = roofs[y0 : y1 + 1, x0 : x1 + 1]
        yy, xx = np.where(window)
        if len(xx) < 25:
            continue
        cities.append((len(xx), x0 + int(xx.min()), y0 + int(yy.min()), x0 + int(xx.max()), y0 + int(yy.max())))
    cities.sort(reverse=True)
    report["roofs"] = cities[:4]

    sprites = {}
    if report["yellow"]:
        x0, y0, x1, y1 = report["yellow"]
        fg = largest_fg(arr, x0 + 4, y0 + 4, x1 - 3, y1 - 3, 48)
        sprites["rider"] = fg
    if cities:
        # expand around the strongest roof cluster and segment the building
        _, x0, y0, x1, y1 = cities[0]
        cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
        fg = largest_fg(arr, max(sx + 4, cx - 110), max(0, cy - 110), min(w - 1, cx + 110), min(h - 1, cy + 110), 36)
        sprites["city"] = fg
    report["sprites"] = sprites

    verdicts = {}
    cw = (w - sx) / 8.0
    ch = h / 6.0
    for name, fg in sprites.items():
        if fg is None:
            verdicts[name] = "MISSING"
            continue
        x0, y0, x1, y1, n = fg
        c0, c1, r0, r1, _, _ = cell_span(x0, y0, x1, y1, sx, w, h)
        # margins to the cell if single
        if c0 == c1 and r0 == r1 and 0 <= c0 < 8 and 0 <= r0 < 6:
            left = (x0 - sx) - c0 * cw
            right = (c0 + 1) * cw - (x1 - sx)
            top = y0 - r0 * ch
            bot = (r0 + 1) * ch - y1
            margin = min(left, right, top, bot)
            ok = margin >= 4
            verdicts[name] = f"{'PASS' if ok else 'TIGHT'} cell r{r0}c{c0} margin {margin:.1f}px size {x1-x0}x{y1-y0} n={n}"
        else:
            verdicts[name] = f"SPILL c{c0}-{c1} r{r0}-{r1} size {x1-x0}x{y1-y0} n={n} bbox {(x0,y0,x1,y1)}"
    report["verdicts"] = verdicts

    # draw overlay copy
    ov = im.copy()
    dr = ImageDraw.Draw(ov)
    for i in range(9):
        x = sx + i * cw
        dr.line([(x, 0), (x, h)], fill=(255, 0, 255), width=1)
    for j in range(7):
        y = j * ch
        dr.line([(sx, y), (w, y)], fill=(255, 0, 255), width=1)
    for name, fg in sprites.items():
        if not fg:
            continue
        x0, y0, x1, y1, _ = fg
        color = (0, 255, 255) if name == "city" else (0, 255, 0)
        dr.rectangle([x0, y0, x1, y1], outline=color, width=2)
    tag = path.rsplit("/", 1)[-1].rsplit(".", 1)[0]
    ov.save(f"{OUT}/overlay-{tag}.jpg", quality=88)
    # tight crops
    for name, fg in sprites.items():
        if not fg:
            continue
        x0, y0, x1, y1, _ = fg
        im.crop((max(0, x0 - 12), max(0, y0 - 12), min(w, x1 + 12), min(h, y1 + 12))).save(
            f"{OUT}/tight-{name}-{tag}.jpg", quality=95
        )
    return report


def main():
    paths = sys.argv[1:]
    for p in paths:
        rep = check(p)
        print(f"\n== {rep['file']} sidebar={rep['sidebar']} yellow={rep['yellow']}")
        print(" roofs", rep["roofs"])
        for k, v in rep["verdicts"].items():
            print(f" {k}: {v}")


if __name__ == "__main__":
    main()
