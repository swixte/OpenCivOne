"""Assembles a city view from parts, as Civ1 assembled its city view from CITYPIX pieces: the land of the city's real
neighbouring cells, walls on the city cell's edges, each improvement on a fixed plot, and houses filling the free plots
by city size; gold clouds hide the edges. Writes OUT-sN.jpg for each size and OUT.json with the improvements' boxes.
Usage: python compose.py OUT"""
import sys, json, math, random
import os
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "../../.."))
from PIL import Image, ImageDraw, ImageFilter, ImageChops

ISO = os.path.join(ROOT, 'themes/ukiyo-e/hd/iso/')
CW, CH = 1600, 1000
W, H = 1000, 500                       # one game cell at view scale
CX, CY = 800, 545                      # the city cell's centre
N = 6                                  # plots per side of the city cell
SW, SH = W / N, H / N


def cell_centre(dx, dy):
    return CX + (dx - dy) * W / 2, CY + (dx + dy) * H / 2


def plot_pt(u, v):
    """A point on the city cell, u along its east axis and v along its south axis, 0..N."""
    return CX + (u - v) * SW / 2, CY - H / 2 + (u + v) * SH / 2


def spr(name):
    return Image.open(f'sprites/{name}.png').convert('RGBA')


def scaled(im, k):
    return im.resize((max(1, round(im.width * k)), max(1, round(im.height * k))), Image.LANCZOS)


# the eight neighbours of Babylon, the same land as the city map (dx east, dy south)
LAND = {(-1, -1): 'river', (0, -1): 'river', (1, -1): 'palms', (-1, 0): 'grass', (1, 0): 'plains',
        (-1, 1): 'hills', (0, 1): 'plains', (1, 1): 'coast', (0, 0): 'city'}


def tinted(path, mul, size=512):
    t = Image.open(ISO + path).convert('RGB').resize((size, size))
    return Image.merge('RGB', [c.point(lambda v, m=m: min(255, int(v * m))) for c, m in zip(t.split(), mul)])


TEX = {'grass': tinted('ground/grass.png', (0.96, 1.06, 0.86)), 'river': tinted('ground/grass.png', (0.94, 1.08, 0.84)),
       'palms': tinted('ground/grass.png', (0.95, 1.03, 0.85)), 'plains': tinted('ground/sand.png', (1.02, 0.98, 0.84)),
       'hills': tinted('ground/grass.png', (1.08, 1.0, 0.78)), 'city': tinted('ground/sand.png', (1.0, 0.95, 0.85))}
SEA, SHALLOW = (40, 96, 124), (66, 134, 152)


def diamond(cx, cy, w=W, h=H, grow=0):
    return [(cx, cy - h / 2 - grow / 2), (cx + w / 2 + grow, cy), (cx, cy + h / 2 + grow / 2), (cx - w / 2 - grow, cy)]


def ground():
    g = Image.new('RGB', (CW, CH), SEA)
    land = Image.new('L', (CW, CH), 0)
    for dx in range(-2, 3):
        for dy in range(-2, 3):
            kind = LAND.get((dx, dy), 'coast' if dx + dy > 1 else 'grass' if dy < 0 or dx < 0 else 'plains')
            cx, cy = cell_centre(dx, dy)
            m = Image.new('L', (CW, CH), 0)
            ImageDraw.Draw(m).polygon(diamond(cx, cy, grow=2), fill=255)
            if kind == 'coast':
                g.paste(SHALLOW if dx + dy <= 2 else SEA, mask=m)
                continue
            tile = Image.new('RGB', (CW, CH))
            for x in range(0, CW, 512):
                for y in range(0, CH, 512):
                    tile.paste(TEX[kind], (x, y))
            g.paste(tile, mask=m)
            land.paste(255, mask=m)
    # roughen the coast so it does not follow the cell diamonds
    import numpy as np
    noise = Image.fromarray((np.random.default_rng(5).random((CH // 50, CW // 50)) * 255).astype('uint8')).resize((CW, CH), Image.BICUBIC)
    soft = np.asarray(land.filter(ImageFilter.GaussianBlur(30)), np.float32)
    wob = (np.asarray(noise.filter(ImageFilter.GaussianBlur(6)), np.float32) - 128) * 0.9
    rough = Image.fromarray(((soft + wob) > 128).astype('uint8') * 255).filter(ImageFilter.ModeFilter(9))
    keep = Image.new('L', (CW, CH), 0)
    for dx in range(-2, 3):
        for dy in range(-2, 3):
            if (dx, dy) in LAND and LAND[(dx, dy)] != 'coast' and abs(dx) + abs(dy) <= 1:
                ImageDraw.Draw(keep).polygon(diamond(*cell_centre(dx, dy)), fill=255)
    land = ImageChops.lighter(rough, keep)
    sea = Image.new('RGB', (CW, CH), SHALLOW)
    sea.paste(SEA, mask=land.filter(ImageFilter.GaussianBlur(40)).point(lambda v: 255 if v < 30 else 0))
    g = Image.composite(g, sea, land)
    # a soft cream shore and an ink coastline where land meets water
    edge = land.filter(ImageFilter.GaussianBlur(9))
    foam = ImageChops.subtract(edge, land).point(lambda v: min(255, v * 3))
    g.paste((214, 212, 186), mask=foam.point(lambda v: v // 2))
    line = land.filter(ImageFilter.FIND_EDGES).point(lambda v: 255 if v > 60 else 0).filter(ImageFilter.MaxFilter(3))
    g.paste((34, 36, 42), mask=line.point(lambda v: v * 2 // 3))
    return g.convert('RGBA'), land


def river(canvas, land):
    a, b = cell_centre(-1, -1), cell_centre(0, -1)
    pts = [cell_centre(-2, -1), a, ((a[0] + b[0]) / 2, b[1] - 120), b, cell_centre(1, -2)]
    path = []
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        for t in range(24):
            u = t / 24
            path.append((x0 + (x1 - x0) * u, y0 + (y1 - y0) * u + math.sin(u * math.pi) * 18))
    layer = Image.new('RGBA', (CW, CH), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for w, c in ((64, (34, 36, 42, 255)), (58, (216, 208, 172, 255)), (46, (60, 120, 160, 255)), (8, (120, 170, 200, 255))):
        d.line(path, fill=c, width=w, joint='curve')
    layer.putalpha(ImageChops.multiply(layer.getchannel('A'), land))
    canvas.alpha_composite(layer)


def fields(canvas):
    plots = [Image.open(ISO + f'fieldplots/{i}.png').convert('RGB').resize((52, 52)) for i in range(8)]
    for dx, dy in ((-1, -1), (0, -1)):
        cx, cy = cell_centre(dx, dy)
        cy += 70
        m = Image.new('L', (CW, CH), 0)
        ImageDraw.Draw(m).polygon(diamond(cx, cy, W * 0.62, H * 0.5), fill=255)
        layer = Image.new('RGB', (CW, CH))
        for i in range(-8, 9):
            for j in range(-8, 9):
                layer.paste(plots[(i * 5 + j * 3) % 8], (int(cx + (i - j) * 26 - 26), int(cy + (i + j) * 13 - 26)))
        canvas.paste(layer, mask=m)


def scatter(items, rnd):
    def load(folder, n):
        return [Image.open(ISO + f'{folder}/{i}.png').convert('RGBA') for i in range(n)]
    palms, trees, hills, tufts = load('jungle', 12), load('broadleaf', 16), load('hills', 16), load('tufts', 25)
    counts = {'palms': 34, 'river': 10, 'grass': 14, 'plains': 12, 'hills': 9}
    for (dx, dy), kind in LAND.items():
        cx, cy = cell_centre(dx, dy)
        for _ in range(counts.get(kind, 0)):
            u, v = rnd.uniform(-0.45, 0.45), rnd.uniform(-0.45, 0.45)
            x, y = cx + (u - v) * W / 2, cy + (u + v) * H / 2
            if kind == 'palms':
                im, k = rnd.choice(palms), 1.0
            elif kind == 'hills':
                im, k = rnd.choice(hills), 1.1
            elif kind == 'river':
                im, k = rnd.choice(palms + trees), 0.8
            else:
                im, k = rnd.choice(tufts + palms[:3]), 0.8
            im = scaled(im, k)
            items.append((y, x, im, (x - im.width / 2, y - im.height + 6)))


# plots: (u, v, size) on the 6x6 city cell; houses take the free single plots in this order as the city grows
PLOTS = {'granary': (0, 0, 2), 'temple': (2, 0, 2), 'barracks': (0, 2, 2), 'palace': (2, 2, 2), 'library': (4, 2, 2),
         'market': (4, 4, 2)}
HOUSES = [(2, 4), (3, 4), (2, 5), (4, 0), (0, 4), (3, 5), (5, 0), (1, 4), (4, 1), (0, 5), (5, 1), (1, 5)]
OUTSIDE = [(6.7, 4.0), (7.0, 5.3), (7.7, 4.5), (6.6, 6.5), (7.5, 6.1), (8.3, 5.2)]   # past the gate, on the plains


def place_plot(items, name, u, v, n, boxes, label=None):
    im = spr(name)
    im = scaled(im, n * SW / im.width)
    bx, by = plot_pt(u + n, v + n)                       # the plot's front corner
    x0, y0 = bx - im.width / 2, by - im.height + n * 4
    items.append((by, bx, im, (x0, y0)))
    if label:
        boxes[label] = [round(x0), round(y0), round(x0 + im.width), round(y0 + im.height)]


def walls(items, boxes):
    k = 0.46
    a, b, tower, gate = (scaled(spr(n), k) for n in ('wall_a', 'wall_b', 'tower', 'gate'))

    def seg_a(u0, v):   # runs down-right: its lower end is its right end
        x, y = plot_pt(u0 + 3, v)
        items.append((y - 1, x, a, (x - a.width + 6, y - a.height + 12)))

    def seg_b(u, v0):   # runs down-left: its lower end is its left end
        x, y = plot_pt(u, v0 + 3)
        items.append((y - 1, x, b, (x - 6, y - b.height + 12)))

    for u0 in (0, 3):
        seg_a(u0, 0)
        seg_a(u0, N)
    seg_b(0, 0)
    seg_b(0, 3)
    seg_b(N, 0)
    x, y = plot_pt(N, 4.5)
    items.append((y + 2, x, gate, (x - gate.width / 2, y - gate.height + 16)))
    boxes['walls'] = [round(x - gate.width / 2), round(y - gate.height + 16), round(x + gate.width / 2), round(y + 16)]
    for u, v in ((0, 0), (N, 0), (0, N), (N, N)):
        x, y = plot_pt(u, v)
        items.append((y + 3, x, tower, (x - tower.width / 2, y - tower.height + 14)))


def gold_leaf(rnd):
    """Gold leaf as on a folding screen: square sheets, each a slightly different tone, with faint seams and grain."""
    import numpy as np
    leaf = 96
    a = np.zeros((CH, CW, 3), np.float32)
    for y in range(0, CH, leaf):
        for x in range(0, CW, leaf):
            k = rnd.uniform(0.9, 1.06)
            a[y:y + leaf, x:x + leaf] = (206 * k, 166 * k, 82 * k)
            a[y:y + 1, x:x + leaf] *= 0.88
            a[y:y + leaf, x:x + 1] *= 0.88
    a *= np.random.default_rng(3).normal(1, 0.035, (CH, CW, 1))
    return Image.fromarray(np.clip(a, 0, 255).astype('uint8'), 'RGB')


def clouds(canvas, rnd):
    tex = gold_leaf(rnd)
    m = Image.new('L', (CW, CH), 0)
    d = ImageDraw.Draw(m)

    def puff(x, y, r):
        for k in range(5):
            a = k / 5 * math.tau
            px, py = x + math.cos(a) * r * .55, y + math.sin(a) * r * .3
            d.ellipse([px - r * .6, py - r * .42, px + r * .6, py + r * .42], fill=255)

    for x in range(-40, CW + 80, 100):
        puff(x, rnd.uniform(-40, -10), rnd.uniform(60, 90))
        puff(x, CH + rnd.uniform(-10, 20), rnd.uniform(70, 100))
    for y in range(-40, CH + 80, 90):
        puff(rnd.uniform(-40, 0), y, rnd.uniform(60, 100))
        puff(CW + rnd.uniform(-10, 30), y, rnd.uniform(60, 100))
    for x, y, r in ((90, 70, 200), (1510, 60, 190), (90, 940, 200), (1520, 940, 210)):
        puff(x, y, r)
    edge = m.filter(ImageFilter.FIND_EDGES).filter(ImageFilter.MaxFilter(3))
    canvas.paste(tex, mask=m)
    canvas.paste((110, 80, 30), mask=edge.point(lambda v: v * 2 // 3))


def build(size, improvements=('palace', 'temple', 'granary', 'barracks', 'library'), building='market'):
    rnd = random.Random(11)
    canvas, land = ground()
    fields(canvas)
    river(canvas, land)
    d = ImageDraw.Draw(canvas)           # lanes of cleared earth along the plot lines
    for t in (2, 4):
        d.line([plot_pt(t, 0), plot_pt(t, N)], fill=(222, 204, 160, 255), width=14)
        d.line([plot_pt(0, t), plot_pt(N, t)], fill=(222, 204, 160, 255), width=14)
    items, boxes = [], {}
    scatter(items, rnd)
    for name in improvements:
        place_plot(items, name, *PLOTS[name], boxes, name)
    if building:
        place_plot(items, building, *PLOTS[building], boxes, 'scaffold')
    for i, (u, v) in enumerate(HOUSES[:size]):
        place_plot(items, f'house{(i * 5) % 12}', u, v, 1, {})
    for i, (u, v) in enumerate(OUTSIDE[:max(0, size - len(HOUSES))]):
        im = scaled(spr(f'house{(i * 7) % 12}'), SW * 0.9 / 260)
        x, y = plot_pt(u, v)
        items.append((y, x, im, (x - im.width / 2, y - im.height + 4)))
    walls(items, boxes)
    for y, x, im, at in sorted(items, key=lambda t: t[0]):
        canvas.alpha_composite(im, (int(at[0]), int(at[1])))
    clouds(canvas, rnd)
    return canvas.convert('RGB'), boxes


if __name__ == '__main__':
    out = sys.argv[1]
    meta = {'width': CW, 'height': CH, 'sizes': {}}
    for size in (3, 8, 16):
        img, boxes = build(size)
        img.save(f'{out}-s{size}.jpg', quality=88)
        meta['sizes'][size] = boxes
    json.dump(meta, open(out + '.json', 'w'), indent=1)
    print('ok')
