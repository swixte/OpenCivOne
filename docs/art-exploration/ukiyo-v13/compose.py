"""A town-screen panorama: the city seen from the front at eye level, built from layers like a stage set.
One painted backdrop, then every improvement as a front-facing cut-out at a fixed place and depth, houses filling
the free places by city size, all drawn back to front so nearer buildings overlap farther ones; gold clouds frame it.
Writes OUT-sN.jpg for each size and OUT.json with each improvement's visible box, front-most last.
Usage: python compose.py OUT"""
import sys, json, math, random
import os
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "../../.."))
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
sys.path.insert(0, os.path.join(ROOT, 'tools/hdart'))
from painted_assets import cut_sprites, key_out
from scipy import ndimage

CW, CH = 1536, 1024
SIZES = (2, 8, 14, 22)


def cut(sheet, names, row_height):
    im = Image.open(sheet)
    sprites = cut_sprites(im, min_area=3000)
    a = key_out(im)[..., 3] > 127
    labels, _ = ndimage.label(ndimage.binary_dilation(a, iterations=2))
    boxes = [sl for i, sl in enumerate(ndimage.find_objects(labels), 1) if (labels[sl] == i).sum() >= 3000]
    order = sorted(range(len(boxes)), key=lambda k: (round((boxes[k][0].start + boxes[k][0].stop) / 2 / row_height), boxes[k][1].start))
    return {n: sprites[k] for n, k in zip(names, order)}


SPR = cut('sheet-buildings.png', ['temple', 'palace', 'library', 'granary', 'barracks', 'market', 'gate'], 340)
SPR.update(cut('sheet-houses.png', ['house0', 'house1', 'house2', 'house3', 'house4', 'house5', 'house6', 'house7',
                                     'palms0', 'house8', 'palms1', 'wall'], 260))
HOUSES = [f'house{i}' for i in range(9)]
LIFE = ['talk', 'donkey', 'water', 'priest', 'dates', 'potter', 'oxcart', 'porters',
        'reedboats', 'sailboat', 'camel', 'well', 'garden', 'bricks', 'sheep', 'crowd']
SPR.update(cut('sheet-life.png', LIFE, 240))
ON_LAND = [n for n in LIFE if n not in ('reedboats', 'sailboat')]

# improvements: (x of centre, baseline y, width) on the 1536x1024 backdrop; a larger baseline is nearer
PLACES = {'temple': (905, 612, 360), 'granary': (455, 672, 330), 'library': (1215, 690, 330),
          'palace': (770, 742, 470), 'barracks': (300, 868, 400), 'market': (1050, 876, 340), 'walls': (1175, 985, 280)}


def landmarks():
    return {n: (px, pb, pw, pw * SPR['gate' if n == 'walls' else n].height / SPR['gate' if n == 'walls' else n].width)
            for n, (px, pb, pw) in PLACES.items()}


def free(x, base, w, h, land):
    """A thing may stand behind a landmark, or in front of it if it hides no more than the landmark's bottom quarter."""
    for px, pb, pw, ph in land.values():
        if abs(x - px) >= (pw + w) / 2 * 0.85:
            continue
        if abs(base - pb) <= 30 or (base > pb and base - h < pb - ph * 0.25):
            return False
    return True


def house_w(base):
    return 88 + (base - 600) * 0.36                       # nearer houses are drawn larger


def core_places(rnd):
    """Houses inside the walls, packed in overlapping rows; the city fills outward from the palace."""
    land, out = landmarks(), []
    for base in range(636, 965, 26):
        w = house_w(base)
        for x in np.arange(120 + rnd.uniform(0, 40), 1470, w * 0.6):
            x = float(x) + rnd.uniform(-12, 12)
            if free(x, base, w, w * 0.72, land):
                out.append((x, base, w))
    return sorted(out, key=lambda p: math.hypot(p[0] - 780, (p[1] - 760) * 2.2) + rnd.uniform(0, 140))


def suburb_places(rnd):
    """Beyond the city: small houses on the plain behind it, and along the river bank on the left."""
    out = []
    for base in (548, 566, 586, 606):
        w = 52 + (base - 540) * 0.5
        for x in np.arange(60 + rnd.uniform(0, 30), 1500, w * 0.75):
            if not (250 < x < 1300 and base > 590):          # the landmarks stand there
                out.append((float(x), base, w))
    for base in range(650, 780, 30):
        out.append((60 + (base - 650) * 0.4 + rnd.uniform(-10, 10), base, house_w(base) * 0.8))
    return sorted(out, key=lambda p: abs(p[0] - 780) * 0.6 + abs(p[1] - 600) + rnd.uniform(0, 300))


def life_places(rnd, n):
    """Groups of people, carts and stalls on the open ground in front of the houses, scaled by depth."""
    land, out = landmarks(), []
    tries = 0
    while len(out) < n and tries < 4000:
        tries += 1
        base, x = rnd.uniform(705, 925), rnd.uniform(330, 1460)
        hw = house_w(base)
        if free(x, base, hw * 0.6, hw * 0.25, land) and all(abs(x - ox) > 70 or abs(base - ob) > 22 for ox, ob, _ in out):
            out.append((x, base, hw))
    return out


def place(canvas, im, x, base, w, boxes=None, label=None, mask=None):
    k = w / im.width
    im = im.resize((round(im.width * k), round(im.height * k)), Image.LANCZOS)
    x0, y0 = round(x - im.width / 2), round(base - im.height)
    canvas.alpha_composite(im, (x0, y0))
    if label is not None:
        boxes[label] = [x0, y0, x0 + im.width, y0 + im.height]
        m = Image.new('L', canvas.size, 0)
        m.paste(im.getchannel('A'), (x0, y0))
        mask[label] = m


def gold_band(canvas, rnd):
    """Raised gold clouds along the bottom and lower corners, as on a folding screen."""
    gold = Image.open('sheet-backdrop.jpg').convert('RGB').crop((20, 20, 300, 160))
    tex = Image.new('RGB', (CW, CH))
    for y in range(0, CH, gold.height):
        for x in range(0, CW, gold.width):
            tex.paste(gold if (x // gold.width + y // gold.height) % 2 else gold.transpose(Image.FLIP_LEFT_RIGHT), (x, y))
    m = Image.new('L', (CW, CH), 0)
    d = ImageDraw.Draw(m)

    def puff(x, y, r):
        for k in range(5):
            t = k / 5 * math.tau
            px, py = x + math.cos(t) * r * .55, y + math.sin(t) * r * .3
            d.ellipse([px - r * .6, py - r * .42, px + r * .6, py + r * .42], fill=255)

    for x in range(-60, CW + 80, 120):
        puff(x, CH + rnd.uniform(15, 40), rnd.uniform(60, 80))
    for x, y, r in ((-30, 980, 200), (1560, 985, 200)):
        puff(x, y, r)
    edge = m.filter(ImageFilter.FIND_EDGES).filter(ImageFilter.MaxFilter(3))
    canvas.paste(tex, mask=m)
    canvas.paste((110, 80, 30), mask=edge.point(lambda v: v * 2 // 3))


def build(size, improvements=('temple', 'granary', 'library', 'palace', 'barracks', 'walls'), building='market'):
    rnd = random.Random(5)
    canvas = Image.open('sheet-backdrop.jpg').convert('RGBA')
    items = []                                            # (baseline, kind, args)
    for name in improvements:
        items.append((PLACES[name][1], name, PLACES[name]))
    if building:
        items.append((PLACES[building][1], 'scaffold', PLACES[building]))
    # growth: houses inside the walls first, then suburbs, then life on the streets and boats on the river
    core = core_places(rnd)
    for i, (x, base, w) in enumerate(core[:size * 4]):
        items.append((base - 1, None, (x, base, w * rnd.uniform(0.88, 1.05), HOUSES[(i * 5 + int(x)) % len(HOUSES)])))
    for i, (x, base, w) in enumerate(suburb_places(rnd)[:max(0, size * 4 - len(core)) + max(0, size - 10) * 4]):
        items.append((base - 1, None, (x, base, w, HOUSES[(i * 7) % len(HOUSES)])))
    for i, (x, base, hw) in enumerate(life_places(rnd, size * 3 // 4 + 1)):
        name = ON_LAND[(i * 5) % len(ON_LAND)]
        k = hw * (0.62 if name in ('dates', 'potter', 'oxcart', 'garden', 'crowd', 'sheep') else 0.44)
        items.append((base + 24, None, (x, base, k, name)))
    for i, (x, base, w) in enumerate(((70, 790, 120), (55, 700, 95), (95, 850, 130))[:max(1, size // 8)]):
        items.append((base, None, (x, base, w, ('reedboats', 'sailboat')[i % 2])))
    for x, base, w in ((110, 700, 190), (1460, 720, 200), (560, 812, 160), (1290, 822, 170)):
        items.append((base - 2, None, (x, base, w, 'palms%d' % (x % 2))))
    boxes, masks = {}, {}
    for base, kind, args in sorted(items, key=lambda t: t[0]):
        if kind is None:
            x, b, w, spr = args
            place(canvas, SPR[spr], x, b, w)
        elif kind == 'walls':
            x, b, w = args
            for wx in (x - 300, x + 290, x - 610, x - 920, x - 1230):   # low wall stretches either side of the gate
                place(canvas, SPR['wall'], wx, b + 8, 340)
            place(canvas, SPR['gate'], x, b, w, boxes, 'walls', masks)
        else:
            src = SPR['market' if kind == 'scaffold' else kind]
            place(canvas, src, args[0], args[1], args[2], boxes, kind, masks)
    gold_band(canvas, rnd)
    # each improvement's box shrunk to the part still visible after everything nearer was drawn
    order = sorted(boxes, key=lambda k: dict((n, p[1]) for n, p in PLACES.items() | {('scaffold', PLACES['market'])})[k])
    covered = Image.new('L', (CW, CH), 0)
    for name in reversed(order):
        vis = np.asarray(masks[name], np.int16) - np.asarray(covered, np.int16)
        ys, xs = np.nonzero(vis > 128)
        if len(xs):
            boxes[name] = [int(np.percentile(xs, 2)), int(np.percentile(ys, 2)), int(np.percentile(xs, 98)), int(np.percentile(ys, 98))]
        covered = Image.fromarray(np.maximum(np.asarray(covered), np.asarray(masks[name])))
    return canvas.convert('RGB'), boxes, order


if __name__ == '__main__':
    out = sys.argv[1]
    meta = {'width': CW, 'height': CH, 'sizes': {}, 'order': None}
    for size in SIZES:
        img, boxes, order = build(size)
        img.save(f'{out}-s{size}.jpg', quality=88)
        meta['sizes'][size] = boxes
        meta['order'] = order
    json.dump(meta, open(out + '.json', 'w'), indent=1)
    print('ok', meta['order'])
