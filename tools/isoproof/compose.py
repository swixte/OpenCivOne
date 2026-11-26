"""Look proof for an isometric (rotated diamond) map view.

Composes one screen from the test scene: one shared warm olive ground for the land, with terrains told apart mostly by
what stands on them (dunes, reeds and pools, stones and lichen, trees, peaks, hills), fields, hamlets and roads around a
city, continuous rivers, a calm sea with foam only at the shore, and a city plate and flags. Everything is drawn back to
front at one scale: a tree clump fills about a tile, a city about two.

Usage: python3 compose.py THEME_DIR OUT.png
"""
import os
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', 'hdart'))
from painted_assets import cut_sprites, cut_object  # noqa: E402

SCENE = ["oooooooooooooooooooooo", "ooooaaaaaaaooooooooooo", "ooaattttttaaoooooooooo", "oogggttffffhhmmooooooo",
         "oogggggffffhmmmmoooooo", "ooggpggggfggghmmoooooo", "oogppgggggggggghoooooo", "ooddpggggggggfffjjoooo",
         "oodddpggggggffjjjjoooo", "ooodddgggggggjjjjooooo", "ooooddgggggggjjooooooo", "oooooooooooooooooooooo"]
# a river (over the ground of its cells) from the hills down to the sea, cell by cell
RIVER = [(12, 4), (12, 5), (11, 5), (11, 6), (10, 6), (10, 7), (9, 7), (9, 8), (8, 8), (8, 9), (8, 10), (8, 11)]
CITIES = [((5, 5), 'Kyoto', 5), ((10, 9), 'Nara', 3)]
ROAD = [(5, 5), (6, 6), (7, 6), (8, 7), (9, 8), (10, 9)]
# the ground of each terrain: one shared olive, nudged a little per terrain (multipliers on the olive base)
TINT = {'g': (1.00, 1.04, 0.92), 'r': (1.00, 1.04, 0.92), 'p': (1.10, 1.04, 0.80), 'd': (1.30, 1.20, 1.00),
        'f': (0.86, 0.94, 0.82), 'j': (0.86, 0.98, 0.80), 'h': (1.02, 0.98, 0.84), 'm': (0.98, 0.96, 0.92),
        't': (1.02, 1.00, 1.02), 's': (0.78, 0.88, 0.80), 'a': None, 'o': None}
TW, TH = 192, 96            # diamond tile size
W, H = 1280, 800
CITY = CITIES[0][0]
PLAYER = (196, 58, 44)      # the player's colour (flags, plate)


def at(x, y):
    if 0 <= y < len(SCENE) and 0 <= x < len(SCENE[0]):
        return SCENE[y][x]
    return 'o'


def noise(shape, scale, seed):
    rng = np.random.default_rng(seed)
    small = rng.random((shape[0] // scale + 2, shape[1] // scale + 2))
    return np.asarray(Image.fromarray((small * 255).astype(np.uint8)).resize((shape[1] + scale, shape[0] + scale), Image.BICUBIC),
                      dtype=np.float64)[:shape[0], :shape[1]] / 255


def calm(tex, mean, contrast):
    """A texture recoloured to a mean colour, its detail kept at a fraction of its contrast."""
    lum = tex.mean(-1, keepdims=True)
    return np.clip(np.array(mean, dtype=np.float64) + (lum - lum.mean()) * contrast, 0, 255)


def main(theme, out):
    gdir = os.path.join(theme, 'hd', 'painted', 'ground')
    raw = os.path.join(theme, 'raw-iso')
    import style
    seam = lambda path: np.asarray(Image.fromarray(style.seamless(Image.open(path).convert('RGB'), 1024)).convert('RGB').resize((512, 512), Image.LANCZOS), dtype=np.float64)
    load = lambda n: np.asarray(Image.open(os.path.join(gdir, n + '.png')).convert('RGB'), dtype=np.float64)
    grass = seam(os.path.join(raw, 'iso_tex_grass.png'))
    sand = seam(os.path.join(raw, 'iso_tex_sand.png'))
    snow = calm(load('snow'), (236, 234, 224), 1.0)
    sea = calm(load('ocean'), (52, 98, 116), 0.28)
    shallow = calm(load('shallow'), (84, 142, 150), 0.35)
    # one inked grass for nearly all land, nudged per terrain; sand for desert, snow for ice
    ground_of = {'g': ('grass', (1.0, 1.0, 1.0)), 'p': ('grass', (1.10, 1.05, 0.82)), 'f': ('grass', (0.86, 0.94, 0.84)),
                 'j': ('grass', (0.86, 0.98, 0.82)), 'h': ('grass', (1.0, 0.97, 0.86)), 'm': ('grass', (0.96, 0.95, 0.92)),
                 't': ('grass', (1.02, 0.98, 1.04)), 's': ('grass', (0.80, 0.88, 0.82)), 'd': ('sand', (1, 1, 1)),
                 'a': ('snow', (1, 1, 1)), 'o': ('sea', (1, 1, 1))}

    def scr(x, y):
        return ox + (x - y) * TW / 2, oy + (x + y) * TH / 2
    ox, oy = 0, 0
    mid = ((CITIES[0][0][0] + CITIES[1][0][0]) / 2 + 0.5, (CITIES[0][0][1] + CITIES[1][0][1]) / 2 + 0.5)
    c = scr(*mid)
    ox, oy = W / 2 - c[0], H / 2 - c[1]

    yy, xx = np.mgrid[0:H, 0:W].astype(np.float64)
    wob = 0.3
    u = (xx - ox) / (TW / 2) + ((noise((H, W), 80, 1) - 0.5) * wob * 2) + ((noise((H, W), 24, 3) - 0.5) * wob * 0.6)
    v = (yy - oy) / (TH / 2) + ((noise((H, W), 80, 2) - 0.5) * wob * 2) + ((noise((H, W), 24, 4) - 0.5) * wob * 0.6)
    gx, gy = (u + v) / 2, (v - u) / 2
    ix, iy = np.floor(gx).astype(int), np.floor(gy).astype(int)
    # each pixel takes one terrain, the one its nearby cells vote for (rounded shapes, hard edges: no blur)
    letters = sorted(ground_of)
    votes = np.zeros((H, W, len(letters)))
    for dj in (-1, 0, 1):
        for di in (-1, 0, 1):
            cxs, cys = ix + di, iy + dj
            d = np.sqrt((gx - (cxs + 0.5)) ** 2 + (gy - (cys + 0.5)) ** 2)
            w = np.clip(1.2 - d * 1.15, 0, None) ** 3
            lab = np.vectorize(at)(cxs, cys)
            for k, ch in enumerate(letters):
                votes[..., k] += w * (lab == ch)
    label = np.array(letters)[votes.argmax(-1)]

    def sample(t):
        n = t.shape[0]
        return t[yy.astype(int) % n, xx.astype(int) % n]
    tex = {'grass': sample(grass), 'sand': sample(sand), 'snow': sample(snow), 'sea': sample(sea)}
    img = np.zeros((H, W, 3))
    group = np.empty((H, W), dtype=object)
    for ch, (name, tint) in ground_of.items():
        m = label == ch
        img[m] = tex[name][m] * np.array(tint)
    kind = np.vectorize(lambda ch: ground_of[ch][0])(label)
    water = kind == 'sea'
    dist = ndimage.distance_transform_edt(water)
    band = np.clip(1 - (dist - 2) / 30, 0, 1)[..., None] * water[..., None]
    img = img * (1 - band) + sample(shallow) * band
    foam = water & (np.abs(dist - 5) < 1.4) & (noise((H, W), 9, 5) > 0.45)
    img[foam] = img[foam] * 0.35 + np.array([236, 232, 214]) * 0.65
    # ink where two kinds of ground meet (and along the coast), like the outlines of the objects
    edge = np.zeros((H, W), dtype=bool)
    for dy_, dx_ in ((0, 1), (1, 0)):
        k2 = np.roll(np.roll(kind, -dy_, 0), -dx_, 1)
        edge |= (kind != k2)
    edge = ndimage.binary_dilation(edge, iterations=1)
    img[edge] = img[edge] * 0.25 + np.array([40, 34, 28]) * 0.75
    landd = ndimage.distance_transform_edt(~water)
    beach = (~water) & (landd < 5) & ~edge
    img[beach] = img[beach] * 0.55 + np.array([214, 198, 150]) * 0.45
    canvas = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).convert('RGBA')
    dr = ImageDraw.Draw(canvas)

    def spline(points, steps=14):
        ctrl = [points[0]] + list(points) + [points[-1]]
        pts = []
        for i in range(1, len(ctrl) - 2):
            p0, p1, p2, p3 = ctrl[i - 1], ctrl[i], ctrl[i + 1], ctrl[i + 2]
            for t in np.linspace(0, 1, steps, endpoint=False):
                t2, t3 = t * t, t * t * t
                pts.append(tuple(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)))
        pts.append(tuple(ctrl[-2]))
        return pts
    rng_r = np.random.default_rng(3)
    # the river winds from cell to cell, with bends between the centres, and runs into the sea
    rp = []
    for k, (x, y) in enumerate(RIVER):
        rp.append(np.array(scr(x + 0.5 + rng_r.uniform(-0.2, 0.2), y + 0.5 + rng_r.uniform(-0.2, 0.2))))
        if k + 1 < len(RIVER):
            nx, ny = RIVER[k + 1]
            m = np.array(scr((x + nx) / 2 + 0.5, (y + ny) / 2 + 0.5))
            side = np.array([-(m - rp[-1])[1], (m - rp[-1])[0]])
            side /= np.linalg.norm(side) + 1e-9
            rp.append(m + side * rng_r.uniform(-22, 22))
    river = spline(rp, 10)
    dr.line(river, fill=(214, 204, 168, 255), width=17, joint='curve')
    dr.line(river, fill=(40, 44, 52, 255), width=12, joint='curve')
    dr.line(river, fill=(86, 134, 160, 255), width=8, joint='curve')
    dr.line(river, fill=(176, 208, 222, 255), width=2, joint='curve')
    # the road between the cities: a wide cream path with a dark edge, curving
    road = [np.array(scr(x + 0.5, y + 0.5)) + np.array([rng_r.uniform(-14, 14), rng_r.uniform(-6, 6)]) for x, y in ROAD]
    road = spline(road, 12)
    dr.line(road, fill=(70, 56, 36, 255), width=20, joint='curve')
    dr.line(road, fill=(222, 204, 158, 255), width=14, joint='curve')

    names = ('trees', 'peaks', 'hills2', 'jungle', 'town', 'tufts', 'tundra', 'reeds', 'ice', 'dunes', 'plots', 'hamlet')
    sets = {n: cut_sprites(Image.open(os.path.join(raw, f'iso_{n}.png'))) for n in names}
    settler = cut_object(Image.open(os.path.join(theme, 'raw', 'unit_settlers.png')))
    warrior = cut_object(Image.open(os.path.join(theme, 'raw', 'unit_militia.png')))
    rng = np.random.default_rng(7)
    ground_items, items = [], []

    def pick(name):
        lst = sets[name]
        return lst[int(rng.integers(len(lst)))]

    def inside(x, y, m=0.18):
        return scr(x + rng.uniform(m, 1 - m), y + rng.uniform(m, 1 - m))
    city_cells = {c for c, _, _ in CITIES}
    road_cells, river_cells = set(ROAD), set(RIVER)
    near_city = {(x + dx, y + dy) for (x, y) in city_cells for dx in range(-2, 3) for dy in range(-2, 3)}
    for y in range(-3, len(SCENE) + 3):
        for x in range(-3, len(SCENE[0]) + 3):
            ch = at(x, y)
            if (x, y) in city_cells:
                continue
            if ch in 'gp' and (x, y) in near_city and (x, y) not in road_cells and (x, y) not in river_cells and rng.random() < 0.75:
                px, py = scr(x + 0.5, y + 0.5)
                ground_items.append((py, px, pick('plots'), TW * 0.98, TH * 0.98))
                if rng.random() < 0.35:
                    px, py = inside(x, y, 0.3)
                    items.append((py, px, pick('hamlet'), rng.uniform(44, 58)))
                continue
            if ch == 'f':
                for _ in range(11):
                    px, py = inside(x, y, 0.05)
                    items.append((py, px, pick('trees'), rng.uniform(150, 180)))
            elif ch == 'j':
                for _ in range(9):
                    px, py = inside(x, y, 0.05)
                    items.append((py, px, pick('jungle'), rng.uniform(150, 180)))
            elif ch == 'm':
                px, py = scr(x + 0.55, y + 0.6)
                items.append((py + 10, px, pick('peaks'), rng.uniform(230, 260)))
            elif ch == 'h':
                for _ in range(2):
                    px, py = inside(x, y, 0.25)
                    items.append((py + 8, px, pick('hills2'), rng.uniform(130, 160)))
            elif ch == 'd':
                for _ in range(int(rng.integers(2, 4))):
                    px, py = inside(x, y)
                    items.append((py, px, pick('dunes'), rng.uniform(70, 100)))
            elif ch == 's':
                for _ in range(int(rng.integers(6, 9))):
                    px, py = inside(x, y)
                    items.append((py, px, pick('reeds'), rng.uniform(30, 44)))
            elif ch == 't':
                for _ in range(int(rng.integers(5, 8))):
                    px, py = inside(x, y)
                    items.append((py, px, pick('tundra'), rng.uniform(22, 32)))
            elif ch == 'a':
                for _ in range(int(rng.integers(3, 5))):
                    px, py = inside(x, y)
                    items.append((py, px, pick('ice'), rng.uniform(50, 76)))
            elif ch in 'gp':
                # 2-4 small props per tile: bushes, a lone tree, rocks, now and then a cottage
                for _ in range(int(rng.integers(2, 5))):
                    px, py = inside(x, y, 0.1)
                    r = rng.random()
                    if r < 0.35:
                        items.append((py, px, sets['trees'][int(rng.integers(0, 4))], rng.uniform(40, 56)))
                    elif r < 0.75:
                        items.append((py, px, pick('tufts'), rng.uniform(22, 32)))
                    elif r < 0.93:
                        items.append((py, px, pick('tundra'), rng.uniform(20, 28)))
                    else:
                        items.append((py, px, pick('hamlet'), rng.uniform(40, 52)))

    plates = []
    for (cx_, cy_), name, size in CITIES:
        tx, ty = scr(cx_ + 0.5, cy_ + 0.5)
        items.append((ty + 30, tx, sets['town'][0 if size > 4 else 2], TW * 1.25))
        plates.append((tx, ty, name, size))
    units = [(settler, CITY[0] - 1, CITY[1] + 1), (warrior, CITIES[1][0][0] + 1, CITIES[1][0][1] - 1)]
    for spr, x, y in units:
        ux, uy = scr(x + 0.5, y + 0.5)
        items.append((uy + 20, ux, spr, None))

    for base, x, spr, w, h in ground_items:
        sp = spr.resize((int(w), int(h)), Image.LANCZOS)
        canvas.alpha_composite(sp, (int(x - sp.width / 2), int(base - sp.height / 2)))
    widest = {id(spr): max(o.width for o in lst) for lst in sets.values() for spr in lst}
    for base, x, spr, width in sorted(items, key=lambda t: t[0]):
        if width is None:
            # a unit stands about half as tall as a city
            k = 112 / spr.height
        else:
            k = width / widest.get(id(spr), spr.width)
        sp = spr.resize((max(1, int(spr.width * k)), max(1, int(spr.height * k))), Image.LANCZOS)
        canvas.alpha_composite(sp, (int(x - sp.width / 2), int(base - sp.height)))

    def flag(x, y, size, pole=1.9):
        d = ImageDraw.Draw(canvas)
        d.line([(x, y), (x, y - size * pole)], fill=(40, 34, 28, 255), width=max(3, size // 7))
        top = y - size * pole
        d.polygon([(x, top), (x + size * 1.2, top + size * 0.12), (x + size, top + size * 0.45), (x + size * 1.2, top + size * 0.8), (x, top + size * 0.7)],
                  fill=PLAYER + (255,), outline=(40, 34, 28, 255), width=2)
    for spr, x, y in units:
        ux, uy = scr(x + 0.5, y + 0.5)
        flag(ux - 34, uy - 52, 20)
    try:
        font = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf', 22)
    except OSError:
        font = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf', 22)
    d = ImageDraw.Draw(canvas)
    for tx, ty, name, size in plates:
        flag(tx + 34, ty - 70, 30, 2.3)
        name_w = d.textlength(name, font=font)
        px0, py0 = tx - (name_w + 44) / 2, ty + 44
        d.rectangle([px0, py0, px0 + 32, py0 + 32], fill=PLAYER + (255,), outline=(40, 34, 28, 255), width=2)
        d.text((px0 + 16, py0 + 16), str(size), fill=(244, 236, 216, 255), font=font, anchor='mm')
        d.rectangle([px0 + 32, py0, px0 + 44 + name_w + 10, py0 + 32], fill=(240, 230, 204, 255), outline=(40, 34, 28, 255), width=2)
        d.text((px0 + 40, py0 + 16), name, fill=(40, 34, 28, 255), font=font, anchor='lm')
    canvas.convert('RGB').save(out)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
