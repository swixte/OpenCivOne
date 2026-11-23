"""Assembles a city view in the proportions of the byobu painting (ukiyo-v8/city-byobu.jpg): a wide, low wall ring
around many small courtyard houses, with the improvements as landmarks on fixed plots. The land comes from the city's
real neighbouring cells; houses fill the free plots by city size; gold clouds hide the edges.
Writes OUT-sN.jpg for each size and OUT.json with the improvements' boxes.
Usage: python compose.py OUT"""
import sys, json, math, random
import os
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "../../.."))
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageChops

ISO = os.path.join(ROOT, 'themes/ukiyo-e/hd/iso/')
CW, CH = 1600, 1000
W, H = 1240, 620                       # the city cell at view scale
CX, CY = 800, 520
N = 12                                 # plots per side: a house is one plot, about 8% of the city's width
SW, SH = W / N, H / N
STREETS = {4, 8}                       # plot rows and columns kept as streets
WALL_H = 30                            # about 4% of the city's height, as in the painting
WALL = (165, 130, 74)


def plot_pt(u, v):
    """A point on the city cell, u along its east axis and v along its south axis, 0..N."""
    return CX + (u - v) * SW / 2, CY - H / 2 + (u + v) * SH / 2


def near(dx, dy, k):
    """The centre of neighbouring cell (dx, dy), pulled k of the way towards the city."""
    return CX + (dx - dy) * W / 2 * k, CY + (dx + dy) * H / 2 * k


def spr(name):
    return Image.open(f'sprites/{name}.png').convert('RGBA')


def scaled(im, k):
    return im.resize((max(1, round(im.width * k)), max(1, round(im.height * k))), Image.LANCZOS)


# Babylon's neighbours, as on the city map (dx east, dy south)
LAND = {(-1, -1): 'river', (0, -1): 'river', (1, -1): 'palms', (-1, 0): 'grass', (1, 0): 'plains',
        (-1, 1): 'hills', (0, 1): 'plains', (1, 1): 'coast'}
GREEN, OCHRE, EARTH, LANE = (150, 146, 86), (196, 168, 104), (190, 160, 104), (216, 194, 146)
SEA, SHALLOW = (44, 86, 140), (70, 120, 160)


def paper(rgb, seed, amp=0.05):
    a = np.ones((CH, CW, 3), np.float32) * np.array(rgb, np.float32)
    n = np.random.default_rng(seed).normal(1, amp, (CH // 4, CW // 4, 1)).astype(np.float32)
    n = np.asarray(Image.fromarray(((n[..., 0] - 1) * 400 + 128).clip(0, 255).astype('uint8')).resize((CW, CH), Image.BICUBIC), np.float32)
    a *= (1 + (n[..., None] - 128) / 400 * amp * 8)
    a *= np.random.default_rng(seed + 1).normal(1, amp / 2, (CH, CW, 1))
    return Image.fromarray(a.clip(0, 255).astype('uint8'), 'RGB')


def diamond(cx, cy, w, h):
    return [(cx, cy - h / 2), (cx + w / 2, cy), (cx, cy + h / 2), (cx - w / 2, cy)]


def ground():
    """Green land all round, water where the neighbours are coast, the river behind the city, cleared earth inside."""
    g = paper(GREEN, 1)
    land = Image.new('L', (CW, CH), 255)
    d = ImageDraw.Draw(land)
    for (dx, dy), kind in LAND.items():
        if kind == 'coast':
            cx, cy = near(dx, dy, 1.0)
            d.polygon(diamond(cx, cy, W * 1.25, H * 1.25), fill=0)
    # wobble the coast so it does not follow the cell diamonds
    noise = Image.fromarray((np.random.default_rng(5).random((CH // 50, CW // 50)) * 255).astype('uint8')).resize((CW, CH), Image.BICUBIC)
    soft = np.asarray(land.filter(ImageFilter.GaussianBlur(26)), np.float32)
    land = Image.fromarray(((soft + (np.asarray(noise, np.float32) - 128) * 0.8) > 128).astype('uint8') * 255).filter(ImageFilter.ModeFilter(9))
    water = paper(SEA, 7)
    water.paste(paper(SHALLOW, 8), mask=land.filter(ImageFilter.GaussianBlur(30)).point(lambda v: 255 if v > 20 else 0))
    g = Image.composite(g, water, land)
    # plains and hills tint their neighbours
    for (dx, dy), kind in LAND.items():
        if kind in ('plains', 'hills'):
            m = Image.new('L', (CW, CH), 0)
            cx, cy = near(dx, dy, 1.0)
            ImageDraw.Draw(m).polygon(diamond(cx, cy, W * 1.1, H * 1.1), fill=150 if kind == 'plains' else 90)
            g.paste(paper(OCHRE, 3), mask=ImageChops.multiply(m.filter(ImageFilter.GaussianBlur(60)), land))
    # the cleared earth of the city, a little larger than the wall ring
    m = Image.new('L', (CW, CH), 0)
    ImageDraw.Draw(m).polygon(diamond(CX, CY, W * 1.08, H * 1.08), fill=255)
    g.paste(paper(EARTH, 4), mask=m.filter(ImageFilter.GaussianBlur(14)))
    # shore and coastline in ink
    edge = land.filter(ImageFilter.FIND_EDGES).point(lambda v: 255 if v > 60 else 0).filter(ImageFilter.MaxFilter(3))
    foam = ImageChops.subtract(land.filter(ImageFilter.GaussianBlur(8)), land).point(lambda v: min(255, v * 3))
    g.paste((226, 220, 196), mask=foam.point(lambda v: v // 2))
    g.paste((40, 38, 36), mask=edge.point(lambda v: v // 2))
    return g.convert('RGBA'), land


def river(canvas, land):
    pts = [near(-2, -1, 0.62), near(-1, -1, 0.6), near(0, -1, 0.62), near(1, -2, 0.62), near(2, -2, 0.75)]
    path = []
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        for t in range(24):
            u = t / 24
            path.append((x0 + (x1 - x0) * u, y0 + (y1 - y0) * u + math.sin(u * math.pi) * 14))
    layer = Image.new('RGBA', (CW, CH), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for w, c in ((92, (40, 38, 36, 255)), (86, (204, 188, 140, 255)), (74, (58, 108, 150, 255))):
        d.line(path, fill=c, width=w, joint='curve')
    for k in range(0, len(path) - 4, 9):     # ripple marks
        (x0, y0), (x1, y1) = path[k], path[k + 3]
        d.line([(x0, y0 - 8), (x1, y1 - 8)], fill=(150, 186, 206, 255), width=2)
    layer.putalpha(ImageChops.multiply(layer.getchannel('A'), land))
    canvas.alpha_composite(layer)


def lanes(canvas):
    """Streets of lighter earth along the street rows and columns, and a road out of the gate."""
    d = ImageDraw.Draw(canvas)
    for s in STREETS:
        d.polygon([plot_pt(s, 0), plot_pt(s + 1, 0), plot_pt(s + 1, N), plot_pt(s, N)], fill=LANE + (255,))
        d.polygon([plot_pt(0, s), plot_pt(0, s + 1), plot_pt(N, s + 1), plot_pt(N, s)], fill=LANE + (255,))
    d.polygon([plot_pt(4, N), plot_pt(5, N), plot_pt(5, N + 3), plot_pt(4, N + 3)], fill=LANE + (255,))


# improvements: (u, v, plots) on the 14x14 city cell, sized as in the painting
PLOTS = {'granary': (0, 0, 3), 'temple': (5, 0, 3), 'barracks': (0, 5, 3), 'palace': (5, 5, 3), 'library': (9, 5, 3),
         'market': (5, 9, 3)}


def free_plots():
    taken = set()
    for u, v, n in PLOTS.values():
        taken |= {(u + i, v + j) for i in range(n) for j in range(n)}
    plots = [(u, v) for u in range(N) for v in range(N) if u not in STREETS and v not in STREETS and (u, v) not in taken]
    return sorted(plots, key=lambda p: (p[0] - N / 2 + .5) ** 2 + (p[1] - N / 2 + .5) ** 2)   # the city grows outward


def place(items, im, u, v, n, boxes=None, label=None, lift=3):
    im = scaled(im, n * SW / im.width)
    bx, by = plot_pt(u + n, v + n)                       # the plot's front corner
    x0, y0 = bx - im.width / 2, by - im.height + n * lift
    items.append((by, im, (x0, y0)))
    if label:
        boxes[label] = [round(x0), round(y0), round(x0 + im.width), round(y0 + im.height)]


def wall_piece(p0, p1, face):
    """A low crenellated wall from p0 to p1 as an image and its position: front face, top, merlons, ink lines."""
    xs, ys = [p0[0], p1[0]], [p0[1], p1[1]]
    x0, y0 = min(xs) - 4, min(ys) - WALL_H - 14
    img = Image.new('RGBA', (int(max(xs) - x0 + 6), int(max(ys) - y0 + 6)), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    a, b = (p0[0] - x0, p0[1] - y0), (p1[0] - x0, p1[1] - y0)
    up = lambda p, h: (p[0], p[1] - h)
    shade = tuple(int(c * face) for c in WALL)
    d.polygon([a, b, up(b, WALL_H), up(a, WALL_H)], fill=shade + (255,))
    for k in range(1, 6):                                # brick courses
        y = WALL_H * k / 6
        d.line([up(a, y), up(b, y)], fill=tuple(int(c * 0.86) for c in shade) + (255,), width=1)
    d.polygon([up(a, WALL_H), up(b, WALL_H), up(b, WALL_H + 4), up(a, WALL_H + 4)], fill=(196, 164, 104, 255))
    length = math.dist(a, b)
    steps = max(2, int(length / 13))
    for k in range(steps):                               # merlons
        t0, t1 = (k + 0.15) / steps, (k + 0.6) / steps
        q0 = (a[0] + (b[0] - a[0]) * t0, a[1] + (b[1] - a[1]) * t0)
        q1 = (a[0] + (b[0] - a[0]) * t1, a[1] + (b[1] - a[1]) * t1)
        d.polygon([up(q0, WALL_H + 4), up(q1, WALL_H + 4), up(q1, WALL_H + 11), up(q0, WALL_H + 11)], fill=(186, 152, 94, 255), outline=(52, 44, 34, 255))
    d.line([a, b], fill=(52, 44, 34, 255), width=2)
    d.line([up(a, WALL_H + 4), up(b, WALL_H + 4)], fill=(52, 44, 34, 255), width=1)
    for p in (a, b):
        d.line([p, up(p, WALL_H + 4)], fill=(52, 44, 34, 255), width=1)
    return img, (x0, y0)


def walls(items, boxes):
    tower = spr('tower')
    gate = spr('gate')
    g0, g1 = 3.0, 6.0                                    # the gate's span on the front-left edge
    def edge(p, q, face, skip=None):
        for k in range(N * 2):
            t0, t1 = k / (N * 2), (k + 1) / (N * 2)
            if skip and skip[0] < t0 * N < skip[1]:
                continue
            a = (p[0] + (q[0] - p[0]) * t0, p[1] + (q[1] - p[1]) * t0)
            b = (p[0] + (q[0] - p[0]) * t1, p[1] + (q[1] - p[1]) * t1)
            img, at = wall_piece(a, b, face)
            items.append((max(a[1], b[1]) - 2, img, at))
    T, R, B, L = plot_pt(0, 0), plot_pt(N, 0), plot_pt(N, N), plot_pt(0, N)
    edge(T, R, 0.82)
    edge(T, L, 0.92)
    edge(R, B, 0.82)
    edge(L, B, 1.0, skip=(g0, g1))
    for u, v in ((0, 0), (N, 0), (N, N), (0, N), (N / 2, 0), (0, N / 2), (N, N / 2), (9.5, N)):
        x, y = plot_pt(u, v)
        im = scaled(tower, SW * 0.95 / tower.width)
        items.append((y + 4, im, (x - im.width / 2, y - im.height + 10)))
    x, y = plot_pt((g0 + g1) / 2, N)
    im = scaled(gate, SW * 3.3 / gate.width)
    items.append((y + 6, im, (x - im.width / 2, y - im.height + 18)))
    boxes['walls'] = [round(x - im.width / 2), round(y - im.height + 18), round(x + im.width / 2), round(y + 18)]


def greenery(items, rnd, plots):
    palms = [Image.open(ISO + f'jungle/{i}.png').convert('RGBA') for i in range(12)]
    for u, v in plots:                                   # gardens with a palm in plots no house has taken yet
        if rnd.random() < 0.55:
            x, y = plot_pt(u + 0.5, v + 0.5)
            im = scaled(rnd.choice(palms), 0.42)
            items.append((y + 4, im, (x - im.width / 2, y - im.height + 6)))
    for _ in range(70):                                  # palm groves outside the walls
        u, v = rnd.uniform(-3, N + 3), rnd.uniform(-3, N + 3)
        if 0 <= u <= N and 0 <= v <= N or v > N + 1 and u > N * 0.6:
            continue
        x, y = plot_pt(u, v)
        im = scaled(rnd.choice(palms), 0.55)
        items.append((y, im, (x - im.width / 2, y - im.height + 6)))


def gold_leaf(rnd):
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
    m = Image.new('L', (CW, CH), 0)
    d = ImageDraw.Draw(m)

    def puff(x, y, r):
        for k in range(5):
            a = k / 5 * math.tau
            px, py = x + math.cos(a) * r * .55, y + math.sin(a) * r * .3
            d.ellipse([px - r * .6, py - r * .42, px + r * .6, py + r * .42], fill=255)

    for x in range(-40, CW + 80, 100):
        puff(x, rnd.uniform(-50, -20), rnd.uniform(60, 90))
        puff(x, CH + rnd.uniform(0, 30), rnd.uniform(70, 100))
    for y in range(-40, CH + 80, 90):
        puff(rnd.uniform(-50, -10), y, rnd.uniform(60, 100))
        puff(CW + rnd.uniform(0, 40), y, rnd.uniform(60, 100))
    for x, y, r in ((70, 60, 210), (1530, 60, 200), (60, 950, 220), (1540, 950, 220)):
        puff(x, y, r)
    edge = m.filter(ImageFilter.FIND_EDGES).filter(ImageFilter.MaxFilter(3))
    canvas.paste(gold_leaf(rnd), mask=m)
    canvas.paste((110, 80, 30), mask=edge.point(lambda v: v * 2 // 3))


def build(size, improvements=('palace', 'temple', 'granary', 'barracks', 'library'), building='market'):
    rnd = random.Random(11)
    canvas, land = ground()
    river(canvas, land)
    lanes(canvas)
    items, boxes = [], {}
    for name in improvements:
        place(items, spr(name), *PLOTS[name], boxes, name)
    if building:
        place(items, spr(building), *PLOTS[building], boxes, 'scaffold')
    plots = free_plots()
    count = min(len(plots), size * 4)
    for i, (u, v) in enumerate(plots[:count]):
        place(items, spr(f'house{(u * 7 + v * 3) % 16}'), u, v, 1, lift=2)
    greenery(items, rnd, plots[count:])
    walls(items, boxes)
    for _, im, at in sorted(items, key=lambda t: t[0]):
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
