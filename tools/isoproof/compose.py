"""Look proof for an isometric (rotated diamond) map view: composes one screen from the test scene with the theme's
ground textures, blended between terrains with soft masks, and real sprites for trees, peaks, hills, jungle, a town
and a unit, drawn back to front.

Usage: python3 compose.py THEME_DIR OUT.png
"""
import os
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', 'hdart'))
from painted_assets import cut_sprites, cut_object  # noqa: E402

SCENE = ["oooooooooooooooooooooo", "oooooaaaaooooooooooooo", "ooootttttaoooooggooooo", "ooogttffhmmooogppgoooo",
         "oogggffhhmmmooggpffooo", "oogpgrrrhhmooggprfffoo", "ooppgrggggmooogprgfjoo", "oodppgrfffjoooogrgjjjo",
         "oodddprssjjjooooorjjoo", "ooodddrsssjooooooooooo", "oooodddrsooooooooooooo", "oooooooooooooooooooooo"]
GROUND = {'d': 'desert', 'p': 'plains', 'g': 'grass', 'f': 'grass', 'h': 'hills', 'm': 'hills', 't': 'tundra',
          'a': 'snow', 's': 'swamp', 'j': 'grass', 'r': 'grass', 'o': 'ocean'}
TW, TH = 128, 64            # diamond tile size
W, H = 1280, 800


def at(x, y):
    if 0 <= y < len(SCENE) and 0 <= x < len(SCENE[0]):
        return SCENE[y][x]
    return 'o'


def noise(shape, scale, seed):
    rng = np.random.default_rng(seed)
    small = rng.random((shape[0] // scale + 2, shape[1] // scale + 2))
    return np.asarray(Image.fromarray((small * 255).astype(np.uint8)).resize((shape[1] + scale, shape[0] + scale), Image.BICUBIC),
                      dtype=np.float64)[:shape[0], :shape[1]] / 255


def main(theme, out):
    ground_dir = os.path.join(theme, 'hd', 'painted', 'ground')
    tex = {n: np.asarray(Image.open(os.path.join(ground_dir, n + '.png')).convert('RGB'), dtype=np.float64)
           for n in set(GROUND.values()) | {'shallow'}}
    # the view: centred on the middle of the island
    cx0, cy0 = 6.5, 5.5
    ox = W / 2 - (cx0 - cy0) * TW / 2
    oy = H / 2 - (cx0 + cy0) * TH / 2
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float64)
    # continuous game coordinates of each pixel, wobbled a little so edges are organic
    wob = 0.32
    u = (xx - ox) / (TW / 2) + ((noise((H, W), 60, 1) - 0.5) * wob * 2) + ((noise((H, W), 17, 3) - 0.5) * wob * 0.6)
    v = (yy - oy) / (TH / 2) + ((noise((H, W), 60, 2) - 0.5) * wob * 2) + ((noise((H, W), 17, 4) - 0.5) * wob * 0.6)
    gx, gy = (u + v) / 2, (v - u) / 2
    ix, iy = np.floor(gx).astype(int), np.floor(gy).astype(int)
    # per pixel terrain letter, and soft blend masks between neighbouring terrains
    keys = sorted(set(GROUND.values()))
    weights = np.zeros((H, W, len(keys)))
    for dj in (-1, 0, 1):
        for di in (-1, 0, 1):
            cxs, cys = ix + di, iy + dj
            d = np.sqrt((gx - (cxs + 0.5)) ** 2 + (gy - (cys + 0.5)) ** 2)
            w = np.clip(1.2 - d * 1.15, 0, None) ** 3
            letters = np.vectorize(lambda a, b: GROUND[at(a, b)])(cxs, cys)
            for k, name in enumerate(keys):
                weights[..., k] += w * (letters == name)
    weights /= weights.sum(-1, keepdims=True) + 1e-9
    # sharpen a little: a soft but narrow transition
    weights = weights ** 2.2
    weights /= weights.sum(-1, keepdims=True)
    img = np.zeros((H, W, 3))
    for k, name in enumerate(keys):
        t = tex[name]
        s = t.shape[0]
        img += t[(yy.astype(int)) % s, (xx.astype(int)) % s] * weights[..., k:k + 1]
    # shallows: a turquoise band of water along the land
    water = weights[..., keys.index('ocean')] > 0.5
    dist = ndimage.distance_transform_edt(water)
    band = np.clip(1 - (dist - 4) / 26, 0, 1)[..., None] * water[..., None]
    sh = tex['shallow']
    img = img * (1 - band) + sh[yy.astype(int) % sh.shape[0], xx.astype(int) % sh.shape[0]] * band
    # an ink line on the coast and a beach
    coast = (dist > 0) & (dist < 2.2)
    img[coast] = img[coast] * 0.3 + np.array([40, 34, 28]) * 0.7
    landdist = ndimage.distance_transform_edt(~water)
    beach = (landdist > 0) & (landdist < 4)
    img[beach] = img[beach] * 0.55 + np.array([232, 220, 186]) * 0.45
    canvas = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).convert('RGBA')

    # rivers: a wavy line through the centres of river cells, on to the sea
    from PIL import ImageDraw
    dr = ImageDraw.Draw(canvas)

    def scr(x, y):
        return ox + (x - y) * TW / 2, oy + (x + y) * TH / 2
    for y in range(len(SCENE)):
        for x in range(len(SCENE[0])):
            if at(x, y) != 'r':
                continue
            for dx, dy in ((1, 0), (0, 1), (-1, 0), (0, -1)):
                if at(x + dx, y + dy) in 'ro':
                    a = scr(x + 0.5, y + 0.5)
                    b = scr(x + 0.5 + dx * (0.5 if at(x + dx, y + dy) == 'o' else 1), y + 0.5 + dy * (0.5 if at(x + dx, y + dy) == 'o' else 1))
                    pts = [(a[0] + (b[0] - a[0]) * t + np.sin(t * 6.28) * 6, a[1] + (b[1] - a[1]) * t + np.cos(t * 6.28) * 3) for t in np.linspace(0, 1, 12)]
                    dr.line(pts, fill=(36, 40, 52, 255), width=9, joint='curve')
                    dr.line(pts, fill=(84, 130, 170, 255), width=6, joint='curve')
                    dr.line(pts, fill=(170, 206, 226, 255), width=1, joint='curve')

    raw = os.path.join(theme, 'raw-iso')
    sets = {n: cut_sprites(Image.open(os.path.join(raw, f'iso_{n}.png'))) for n in ('trees', 'peaks', 'hills', 'jungle', 'town')}
    # hills: grassy olive-green instead of bare earth
    greened = []
    for spr in sets['hills']:
        a = np.asarray(spr.convert('RGBA')).astype(float)
        lum = (a[..., 0] * 0.3 + a[..., 1] * 0.59 + a[..., 2] * 0.11) / 255
        xs = np.linspace(0, 1, a.shape[1])[None, :]
        t = np.clip(lum * 1.2 - 0.15 * (xs - 0.5), 0, 1)[..., None]
        col = np.array([92, 96, 52.]) + (np.array([172, 166, 92.]) - np.array([92, 96, 52.])) * t
        dark = (lum < 0.28)[..., None]
        a[..., :3] = np.where(dark, np.array([44, 40, 28.]) + (col - np.array([44, 40, 28.])) * (lum[..., None] / 0.28), col)
        greened.append(Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), 'RGBA'))
    sets['hills'] = greened
    settler = cut_object(Image.open(os.path.join(theme, 'raw', 'unit_settlers.png')))

    rng = np.random.default_rng(7)
    items = []          # (base y, x, sprite, width)

    def put(name, x, y, width, i=None):
        lst = sets[name]
        spr = lst[i if i is not None else rng.integers(len(lst))]
        items.append((y, x, spr, width))

    def inside(x, y):
        # a random point inside the diamond of cell (x, y), in game coordinates
        return x + rng.uniform(0.15, 0.85), y + rng.uniform(0.15, 0.85)
    for y in range(-2, len(SCENE) + 2):
        for x in range(-2, len(SCENE[0]) + 2):
            c = at(x, y)
            if c == 'f':
                for _ in range(9):
                    px, py = scr(*inside(x, y))
                    put('trees', px, py, rng.uniform(34, 52), i=int(rng.choice([0, 1, 2, 3, 4, 12, 14, 15])))
            elif c == 'j':
                for _ in range(6):
                    px, py = scr(*inside(x, y))
                    put('jungle', px, py, rng.uniform(38, 56))
            elif c == 'm':
                px, py = scr(x + 0.55, y + 0.55)
                put('peaks', px, py + 8, rng.uniform(140, 170))
            elif c == 'h':
                for _ in range(2):
                    px, py = scr(*inside(x, y))
                    put('hills', px, py + 6, rng.uniform(70, 96))
            elif c in 'gpr':
                for _ in range(rng.integers(1, 4) if c != 'p' else rng.integers(0, 2)):
                    px, py = scr(*inside(x, y))
                    put('trees', px, py, rng.uniform(16, 26), i=int(rng.integers(0, 4)))
    tx, ty = scr(5.5, 6.5)
    items.append((ty + 22, tx, sets['town'][0], 190))
    ux, uy = scr(4.5, 4.5)
    items.append((uy + 14, ux, settler, 40))
    for base, x, spr, width in sorted(items, key=lambda t: t[0]):
        h = int(spr.height * width / spr.width)
        s = spr.resize((int(width), max(1, h)), Image.LANCZOS)
        canvas.alpha_composite(s, (int(x - s.width / 2), int(base - s.height)))
    canvas.convert('RGB').save(out)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
