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

SCENE = ["oooooooooooooooooooooo", "oooooaaaaooooooooooooo", "ooootttttaoooooggooooo", "ooogttffhmmooogppgoooo",
         "oogggffhhmmmooggpffooo", "oogpgrrrhhmooggprfffoo", "ooppgrggggmooogprgfjoo", "oodppgrfffjoooogrgjjjo",
         "oodddprssjjjooooorjjoo", "ooodddrsssjooooooooooo", "oooodddrsooooooooooooo", "oooooooooooooooooooooo"]
# the ground of each terrain: one shared olive, nudged a little per terrain (multipliers on the olive base)
TINT = {'g': (1.00, 1.04, 0.92), 'r': (1.00, 1.04, 0.92), 'p': (1.10, 1.04, 0.80), 'd': (1.30, 1.20, 1.00),
        'f': (0.86, 0.94, 0.82), 'j': (0.86, 0.98, 0.80), 'h': (1.02, 0.98, 0.84), 'm': (0.98, 0.96, 0.92),
        't': (1.02, 1.00, 1.02), 's': (0.78, 0.88, 0.80), 'a': None, 'o': None}
TW, TH = 192, 96            # diamond tile size
W, H = 1280, 800
CITY = (6, 5)               # cell of the city (x, y)
CITY_NAME, CITY_SIZE = 'Kyoto', 5
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
    load = lambda n: np.asarray(Image.open(os.path.join(gdir, n + '.png')).convert('RGB'), dtype=np.float64)
    olive = calm(load('grass'), (128, 120, 72), 0.85)
    snow = calm(load('snow'), (232, 230, 220), 0.9)
    sea = calm(load('ocean'), (52, 98, 116), 0.28)
    shallow = calm(load('shallow'), (84, 142, 150), 0.35)

    def scr(x, y):
        return ox + (x - y) * TW / 2, oy + (x + y) * TH / 2
    # the view: centred a little above the city
    ox, oy = 0, 0
    c = scr(CITY[0] + 0.5, CITY[1] + 0.5)
    ox, oy = W / 2 - c[0], H / 2 - c[1] + 40

    yy, xx = np.mgrid[0:H, 0:W].astype(np.float64)
    wob = 0.3
    u = (xx - ox) / (TW / 2) + ((noise((H, W), 80, 1) - 0.5) * wob * 2) + ((noise((H, W), 24, 3) - 0.5) * wob * 0.6)
    v = (yy - oy) / (TH / 2) + ((noise((H, W), 80, 2) - 0.5) * wob * 2) + ((noise((H, W), 24, 4) - 0.5) * wob * 0.6)
    gx, gy = (u + v) / 2, (v - u) / 2
    ix, iy = np.floor(gx).astype(int), np.floor(gy).astype(int)
    letters = sorted(TINT)
    weights = np.zeros((H, W, len(letters)))
    for dj in (-1, 0, 1):
        for di in (-1, 0, 1):
            cxs, cys = ix + di, iy + dj
            d = np.sqrt((gx - (cxs + 0.5)) ** 2 + (gy - (cys + 0.5)) ** 2)
            w = np.clip(1.2 - d * 1.15, 0, None) ** 3
            lab = np.vectorize(at)(cxs, cys)
            for k, ch in enumerate(letters):
                weights[..., k] += w * (lab == ch)
    weights = (weights / (weights.sum(-1, keepdims=True) + 1e-9)) ** 2.2
    weights /= weights.sum(-1, keepdims=True)

    def sample(t):
        s = t.shape[0]
        return t[yy.astype(int) % s, xx.astype(int) % s]
    o_s, sn_s, sea_s, sh_s = sample(olive), sample(snow), sample(sea), sample(shallow)
    img = np.zeros((H, W, 3))
    for k, ch in enumerate(letters):
        if ch == 'o':
            img += sea_s * weights[..., k:k + 1]
        elif ch == 'a':
            img += sn_s * weights[..., k:k + 1]
        else:
            img += o_s * np.array(TINT[ch]) * weights[..., k:k + 1]
    water = weights[..., letters.index('o')] > 0.5
    dist = ndimage.distance_transform_edt(water)
    band = np.clip(1 - (dist - 2) / 30, 0, 1)[..., None] * water[..., None]
    img = img * (1 - band) + sh_s * band
    # foam only at the shore: a broken pale line just off the coast
    foam = water & (np.abs(dist - 5) < 1.4) & (noise((H, W), 9, 5) > 0.45)
    img[foam] = img[foam] * 0.35 + np.array([236, 232, 214]) * 0.65
    coast = water & (dist < 2.2)
    img[coast] = img[coast] * 0.25 + np.array([40, 34, 28]) * 0.75
    landd = ndimage.distance_transform_edt(~water)
    beach = (~water) & (landd < 5)
    img[beach] = img[beach] * 0.55 + np.array([214, 198, 150]) * 0.45
    canvas = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).convert('RGBA')
    dr = ImageDraw.Draw(canvas)

    # rivers: one continuous smooth course from the spring through every river cell to the sea
    river = {(x, y) for y in range(len(SCENE)) for x in range(len(SCENE[0])) if at(x, y) == 'r'}

    def neighbours(p):
        return [(p[0] + dx, p[1] + dy) for dx, dy in ((1, 0), (0, 1), (-1, 0), (0, -1))]
    courses, seen = [], set()
    for start in sorted(river):
        if start in seen or sum(n in river for n in neighbours(start)) != 1:
            continue
        path, p = [start], start
        seen.add(p)
        while True:
            nxt = [n for n in neighbours(p) if n in river and n not in seen]
            if not nxt:
                sea_n = [n for n in neighbours(p) if at(*n) == 'o']
                if sea_n:
                    path.append(sea_n[0])
                break
            p = nxt[0]
            seen.add(p)
            path.append(p)
        courses.append(path)
    rng_r = np.random.default_rng(3)
    for path in courses:
        ctrl = [np.array(scr(x + 0.5 + rng_r.uniform(-0.12, 0.12), y + 0.5 + rng_r.uniform(-0.12, 0.12))) for x, y in path]
        if at(*path[-1]) == 'o':
            ctrl[-1] = (ctrl[-1] + ctrl[-2]) / 2
        ctrl = [ctrl[0]] + ctrl + [ctrl[-1]]
        pts = []
        for i in range(1, len(ctrl) - 2):
            p0, p1, p2, p3 = ctrl[i - 1], ctrl[i], ctrl[i + 1], ctrl[i + 2]
            for t in np.linspace(0, 1, 14, endpoint=False):
                t2, t3 = t * t, t * t * t
                pts.append(tuple(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)))
        pts.append(tuple(ctrl[-2]))
        dr.line(pts, fill=(214, 204, 168, 255), width=14, joint='curve')      # pale bank
        dr.line(pts, fill=(40, 44, 52, 255), width=9, joint='curve')          # ink
        dr.line(pts, fill=(86, 134, 160, 255), width=6, joint='curve')        # water
        dr.line(pts, fill=(176, 208, 222, 255), width=1, joint='curve')       # glint

    raw = os.path.join(theme, 'raw-iso')
    names = ('trees', 'peaks', 'hills2', 'jungle', 'town', 'tufts', 'tundra', 'reeds', 'ice', 'dunes', 'fields', 'hamlet')
    sets = {n: cut_sprites(Image.open(os.path.join(raw, f'iso_{n}.png'))) for n in names}
    settler = cut_object(Image.open(os.path.join(theme, 'raw', 'unit_settlers.png')))
    rng = np.random.default_rng(7)
    ground_items, items = [], []        # (sort y, x, sprite, width): flat decals first, then tall props back to front

    def pick(name):
        lst = sets[name]
        return lst[int(rng.integers(len(lst)))]

    def inside(x, y, m=0.18):
        return scr(x + rng.uniform(m, 1 - m), y + rng.uniform(m, 1 - m))

    # roads from the city: two lanes running out across open land
    road_cells = []
    for dx, dy, n in ((1, 0, 3), (0, 1, 3), (-1, -1, 2)):
        x, y = CITY
        for _ in range(n):
            x, y = x + dx, y + dy
            if at(x, y) in 'gprdh':
                road_cells.append(((x - dx, y - dy), (x, y)))
    for a, b in road_cells:
        pa, pb = np.array(scr(a[0] + 0.5, a[1] + 0.5)), np.array(scr(b[0] + 0.5, b[1] + 0.5))
        mid = (pa + pb) / 2 + np.array([rng.uniform(-10, 10), rng.uniform(-5, 5)])
        line = [tuple(pa), tuple(mid), tuple(pb)]
        dr.line(line, fill=(70, 58, 40, 255), width=9, joint='curve')
        dr.line(line, fill=(206, 186, 140, 255), width=6, joint='curve')

    # around the city: fields and hamlets on open land within its working radius
    for y in range(CITY[1] - 2, CITY[1] + 3):
        for x in range(CITY[0] - 2, CITY[0] + 3):
            if (x, y) == CITY or at(x, y) not in 'gpr':
                continue
            if rng.random() < 0.6:
                px, py = scr(x + 0.5, y + 0.5)
                ground_items.append((py, px, pick('fields'), TW * 0.86, TH * 0.43))
            if rng.random() < 0.45:
                px, py = inside(x, y, 0.3)
                items.append((py, px, pick('hamlet'), rng.uniform(34, 46)))

    for y in range(-3, len(SCENE) + 3):
        for x in range(-3, len(SCENE[0]) + 3):
            ch = at(x, y)
            if (x, y) == CITY:
                continue
            if ch == 'f':
                for _ in range(7):
                    px, py = inside(x, y, 0.1)
                    items.append((py, px, pick('trees'), rng.uniform(120, 150)))
            elif ch == 'j':
                for _ in range(6):
                    px, py = inside(x, y, 0.1)
                    items.append((py, px, pick('jungle'), rng.uniform(120, 150)))
            elif ch == 'm':
                px, py = scr(x + 0.55, y + 0.6)
                items.append((py + 10, px, pick('peaks'), rng.uniform(220, 250)))
            elif ch == 'h':
                for _ in range(2):
                    px, py = inside(x, y, 0.25)
                    items.append((py + 8, px, pick('hills2'), rng.uniform(120, 150)))
            elif ch == 'd':
                for _ in range(int(rng.integers(2, 4))):
                    px, py = inside(x, y)
                    items.append((py, px, pick('dunes'), rng.uniform(60, 90)))
            elif ch == 's':
                for _ in range(int(rng.integers(6, 9))):
                    px, py = inside(x, y)
                    items.append((py, px, pick('reeds'), rng.uniform(30, 44)))
            elif ch == 't':
                for _ in range(int(rng.integers(5, 8))):
                    px, py = inside(x, y)
                    items.append((py, px, pick('tundra'), rng.uniform(18, 28)))
            elif ch == 'a':
                for _ in range(int(rng.integers(2, 4))):
                    px, py = inside(x, y)
                    items.append((py, px, pick('ice'), rng.uniform(44, 70)))
            elif ch in 'gpr':
                for _ in range(int(rng.integers(0, 3))):
                    px, py = inside(x, y)
                    items.append((py, px, sets['trees'][int(rng.integers(0, 4))], rng.uniform(30, 44)))
                for _ in range(int(rng.integers(4, 8))):
                    px, py = inside(x, y, 0.08)
                    items.append((py, px, pick('tufts'), rng.uniform(14, 22)))

    tx, ty = scr(CITY[0] + 0.5, CITY[1] + 0.5)
    city = sets['town'][0]
    items.append((ty + 34, tx, city, TW * 1.45))
    ux, uy = scr(CITY[0] - 1 + 0.5, CITY[1] + 1 + 0.5)
    items.append((uy + 16, ux, settler, 58))

    for base, x, spr, w, h in ground_items:
        s = spr.resize((int(w), int(h)), Image.LANCZOS)
        canvas.alpha_composite(s, (int(x - s.width / 2), int(base - s.height / 2)))
    # one scale per sprite set: the widest sprite of a set gets the width asked for, the others keep their size relative
    # to it, so a single tree stays smaller than a clump
    widest = {id(spr): max(o.width for o in lst) for lst in sets.values() for spr in lst}
    for base, x, spr, width in sorted(items, key=lambda t: t[0]):
        k = width / widest.get(id(spr), spr.width)
        s = spr.resize((max(1, int(spr.width * k)), max(1, int(spr.height * k))), Image.LANCZOS)
        canvas.alpha_composite(s, (int(x - s.width / 2), int(base - s.height)))

    # flags in the player's colour: on the city and on the unit
    def flag(x, y, size):
        d = ImageDraw.Draw(canvas)
        d.line([(x, y), (x, y - size * 1.9)], fill=(40, 34, 28, 255), width=max(2, size // 8))
        top = y - size * 1.9
        d.polygon([(x, top), (x + size * 1.15, top + size * 0.15), (x + size * 0.95, top + size * 0.45), (x + size * 1.15, top + size * 0.75), (x, top + size * 0.65)],
                  fill=PLAYER + (255,), outline=(40, 34, 28, 255))
    flag(tx + 14, ty - 112, 20)
    flag(ux - 16, uy - 46, 13)

    # the city plate: a size number in the player's colour and the name on cream
    try:
        font = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf', 22)
    except OSError:
        font = ImageFont.load_default()
    d = ImageDraw.Draw(canvas)
    name_w = d.textlength(CITY_NAME, font=font)
    px0, py0 = tx - (name_w + 44) / 2, ty + 40
    d.rectangle([px0, py0, px0 + 32, py0 + 32], fill=PLAYER + (255,), outline=(40, 34, 28, 255), width=2)
    d.text((px0 + 16, py0 + 16), str(CITY_SIZE), fill=(244, 236, 216, 255), font=font, anchor='mm')
    d.rectangle([px0 + 32, py0, px0 + 44 + name_w + 10, py0 + 32], fill=(240, 230, 204, 255), outline=(40, 34, 28, 255), width=2)
    d.text((px0 + 40, py0 + 16), CITY_NAME, fill=(40, 34, 28, 255), font=font, anchor='lm')
    canvas.convert('RGB').save(out)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
