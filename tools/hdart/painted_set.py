"""Writes the 4x sprite set that goes with the painted map: units, resources, cities, walls, fortress and huts from the
painted objects (assets/painted/objects, see painted_assets.py), and the remaining map sprites (roads, railroads,
irrigation, mines, pollution, fog, explosions, shield bonus) from the drawn 2x set at twice the size.

Terrain tiles are left out: the painted map replaces them. The output has scale.txt with 4.

Usage: python3 painted_set.py [OUTPUT_DIR] [OBJECTS_DIR]     (defaults: assets/hd4 and assets/painted/objects)

With `print 1` in the style.txt next to the objects folder (the painted art folder), the set is drawn for a woodblock
print map: no cast shadows, every unit and object inside a dark key line and a cream outline so it reads on any ground,
units a little smaller, the flag and the shield marker drawn with key lines, and the city on a ring of light cleared earth.
"""

import os
import sys
import numpy as np
from PIL import Image

import common as c
import overlays as o
import style
import units as u

HERE = os.path.dirname(os.path.abspath(__file__))
S = 4                       # HD pixels per original pixel
UNIT = 15 * S               # units, resources and the city are 15x15 original pixels
CITY_UP = 16                # HD rows of the town that rise into the cell above (a multiple of S)
RESOURCES = ['oasis', 'horses', 'wheat', 'pheasant', 'coal', 'gold', 'caribou', 'seal', 'peat', 'gems', 'fish', 'fish']
SHIPS = range(16, 25)
AIRCRAFT = (14, 15, 25)

OBJECTS = os.path.join(HERE, '..', '..', 'assets', 'painted', 'objects')
PRINT = False
KEY_LINE = (30, 26, 22, 255)
CREAM = (244, 236, 216, 255)


def print_style(objects):
    path = os.path.join(objects, '..', 'style.txt')
    if not os.path.exists(path):
        return False
    return any(line.split()[:2] == ['print', '1'] for line in open(path) if line.strip())


def outlined(img, key=3, cream=3):
    """The image inside a dark key line (key pixels) and a cream outline outside it (cream pixels)."""
    from scipy import ndimage
    a = img[..., 3] > 96
    inner = ndimage.binary_dilation(a, iterations=key) & ~a
    outer = ndimage.binary_dilation(a, iterations=key + cream) & ~a & ~inner
    out = img.copy()
    out[outer] = CREAM
    out[inner] = KEY_LINE
    keep = img[..., 3] > 0
    # the art over its own soft edge, so the key line meets it cleanly
    out[keep & a] = img[keep & a]
    return out
UI = os.path.join(HERE, '..', '..', 'assets', 'painted', 'ui')


def obj(name):
    return np.asarray(Image.open(os.path.join(OBJECTS, name + '.png')).convert('RGBA'))


def up2(img):
    """Drawn 2x art at 4x."""
    h, w = img.shape[:2]
    return np.asarray(Image.fromarray(img).resize((w * 2, h * 2), Image.LANCZOS))


def icon(name, size, bottom=None, shadow=True):
    """A painted object fitted into a size x size sprite, standing on its bottom edge, with a soft shadow."""
    img = np.zeros((UNIT, UNIT, 4), dtype=np.uint8)
    if PRINT:
        # room for the outline inside the sprite
        size = min(size, UNIT - 14)
    fitted = style.fit(obj(name), width=size, height=size)
    bottom = UNIT - 2 if bottom is None else bottom
    if PRINT:
        style.paste(img, fitted, UNIT / 2, min(bottom, UNIT - 7))
        return outlined(img)
    if shadow:
        style.shadow(img, UNIT / 2, bottom - 1, fitted.shape[1] * 0.42, alpha=90)
    style.paste(img, fitted, UNIT / 2, bottom)
    return img


def unit(j):
    """Unit j in UnitTypeEnum order, with a small flag in the player's colors (key colors) at the top left."""
    img = np.zeros((UNIT, UNIT, 4), dtype=np.uint8)
    if PRINT:
        return print_unit(j)
    fitted = style.fit(obj('unit_' + u.UNIT_NAMES[j]), width=UNIT - 4, height=UNIT - 6)
    if j in AIRCRAFT:
        # flying: the shadow falls on the ground further away
        style.shadow(img, UNIT / 2 + 6, UNIT - 3, fitted.shape[1] * 0.3, alpha=70)
        style.paste(img, fitted, UNIT / 2, UNIT - 8)
    else:
        if j not in SHIPS:
            style.shadow(img, UNIT / 2, UNIT - 3, fitted.shape[1] * 0.42, alpha=90)
        style.paste(img, fitted, UNIT / 2, UNIT - 2)
    flag = c.Sprite(UNIT, UNIT, u.M)
    flag.line(4.5, 1, 4.5, 21, 2, 'wood', 0)
    flag.rect(6, 1, 18, 9, 'P', 0.3)
    flag.rect(6, 9, 18, 12, 'S', 0)
    f = flag.render()
    shown = f[..., 3] > 0
    img[shown] = f[shown]
    return img


def print_flag():
    """A small flag in the player's colors with key lines, on a dark pole, for the top left of a unit."""
    flag = c.Sprite(UNIT, UNIT, u.M)
    flag.line(5.5, 2, 5.5, 22, 2, 'dark', 0)
    flag.rect(7, 2, 19, 9, 'P', 0.3)
    flag.rect(7, 9, 19, 12, 'S', 0)
    return flag.render(outline=KEY_LINE, auto_light=False)


def print_unit(j):
    """A unit for the print map: about four fifths of the cell, in a key line and a cream outline, with its flag."""
    img = np.zeros((UNIT, UNIT, 4), dtype=np.uint8)
    fitted = style.fit(obj('unit_' + u.UNIT_NAMES[j]), width=UNIT - 14, height=UNIT - 16)
    bottom = UNIT - 9 if j in AIRCRAFT else UNIT - 6
    style.paste(img, fitted, UNIT / 2 + 3, bottom)
    img = outlined(img)
    f = print_flag()
    shown = f[..., 3] > 0
    img[shown] = f[shown]
    return img


def print_shield_bonus():
    """The shield marker for the print map: a small muted shield seal with key lines, quieter than the units."""
    s = c.Sprite(14, 14, dict(seal=c.ramp('#7a3a26', '#9a4e34', '#b0654a', '#c47c5e', '#d4967a'),
                              ochre=c.ramp('#8a6a30', '#a8853e', '#c09c56', '#d2b46e', '#e2cb8e')))
    s.poly([(2, 2), (12, 2), (12, 7), (7, 12), (2, 7)], 'seal', 0)
    s.poly([(5, 4), (9, 4), (9, 7), (7, 9), (5, 7)], 'ochre', 0)
    return s.render(outline=KEY_LINE, auto_light=False)


def print_city():
    """The town for the print map, on a ring of light cleared earth that fades into the ground around it, rising into the
    cell above. The game writes the city size at the bottom left of the cell on a print map (from original pixel 1 or 3,
    9), so a small badge sits there, over the edge of the town as on a printed map."""
    h = UNIT + CITY_UP
    img = np.zeros((h, UNIT, 4), dtype=np.uint8)
    y, x = np.mgrid[0:h, 0:UNIT] + 0.5
    d = np.sqrt(((x - UNIT * 0.5) / (UNIT * 0.52)) ** 2 + ((y - (h - UNIT * 0.42)) / (UNIT * 0.44)) ** 2)
    ring = np.clip((1 - d) / 0.3, 0, 1)
    img[..., 0], img[..., 1], img[..., 2] = 240, 230, 200
    img[..., 3] = np.rint(ring * 230).astype(np.uint8)
    town = np.zeros((h, UNIT, 4), dtype=np.uint8)
    fitted = style.fit(obj('town'), width=UNIT - 4, height=h - 8)
    style.paste(town, fitted, UNIT / 2 + 1, h - 6)
    town = outlined(town, key=2, cream=1)
    shown = town[..., 3] > 0
    img[shown] = town[shown]
    badge = c.Sprite(UNIT, h, {'P': 'P', 'S': 'S'})
    # the sprite is drawn one original pixel in from the cell corner, and the game fills original pixels 2..13 of the
    # cell with the player's colors, so the badge stays inside that square
    badge.rect(5, CITY_UP + 29, 35, CITY_UP + 52, 'S', 0)
    badge.rect(7, CITY_UP + 31, 33, CITY_UP + 50, 'P', 0.5)
    b = badge.render(outline=KEY_LINE)
    shown = b[..., 3] > 0
    img[shown] = b[shown]
    return img


def city():
    """The town, rising CITY_UP rows into the cell above, with a badge in the player's colors behind the size number."""
    h = UNIT + CITY_UP
    img = np.zeros((h, UNIT, 4), dtype=np.uint8)
    fitted = style.fit(obj('town'), width=UNIT, height=h - 2)
    style.shadow(img, UNIT / 2, h - 3, UNIT * 0.45, alpha=90)
    style.paste(img, fitted, UNIT / 2, h - 1)
    # with the painted map the game writes the size at original pixels (2..9, 1..6) of this sprite, in its top left corner
    badge = c.Sprite(UNIT, h, {'P': 'P', 'S': 'S'})
    badge.rect(6, CITY_UP + 1, 39, CITY_UP + 29, 'S', 0)
    badge.rect(8, CITY_UP + 3, 37, CITY_UP + 27, 'P', 0.5)
    b = badge.render(outline=(20, 16, 12, 255))
    shown = b[..., 3] > 0
    img[shown] = b[shown]
    return img


def sprites():
    """Yields (sheet, x, y, suffix, image) for every sprite of the set."""
    for i in range(8):
        yield 'SP257', i * 16, 48, '', up2(o.road(i + 1))
        yield 'SP257', 128 + i * 16, 96, '', up2(o.railroad(i + 1))
    yield 'SP257', 64, 32, '', up2(o.irrigation())
    yield 'SP257', 80, 32, '', up2(o.mine())
    yield 'SP257', 96, 32, '', up2(o.pollution())
    for i, name in enumerate(RESOURCES):
        yield 'SP257', i * 16 + 1, 113, '', icon('res_' + name, 34, UNIT - 10)
    yield 'SP257', 12 * 16 + 1, 113, f'_t{CITY_UP // S}_k0-5', print_city() if PRINT else city()
    yield 'SP257', 13 * 16 + 1, 113, '', icon('walls', UNIT, UNIT - 1, shadow=False)
    yield 'SP257', 14 * 16 + 1, 113, '', icon('fortress', UNIT - 6)
    yield 'SP257', 15 * 16 + 1, 113, '', icon('hut', 44)
    yield 'SP257', 153, 41, '', up2(print_shield_bonus() if PRINT else o.shield_bonus())
    for i, direction in enumerate((1, 3, 5, 7)):
        yield 'SP257', 80 + i * 16, 128, '', up2(o.fog(direction))
    for i in range(8):
        yield 'SP257', i * 16 + 1, 97, '', up2(o.explosion(i))
    for j in range(len(u.UNIT_NAMES)):
        yield 'SP257', (j % 20) * 16 + 1, (j // 20) * 16 + 161, '', unit(j)


def tile(name, width, height, scale, mean, spread, tint=(1.0, 1.0, 1.0)):
    """A width x height tile that repeats without seams, from part of a seamless interface texture shown `scale` times its
    size, with its brightness set to `mean` and its contrast to `spread` (standard deviation)."""
    tex = Image.open(os.path.join(UI, name + '.png')).convert('RGB')
    cw, ch = round(width / scale), round(height / scale)
    crop = tex.crop((0, 0, cw, ch)).resize((width, height), Image.LANCZOS)
    a = np.asarray(crop, dtype=np.float64)
    # blend with a half-shifted copy so the tile repeats without seams in both directions
    b = np.roll(np.roll(a, height // 2, 0), width // 2, 1)
    y, x = np.mgrid[0:height, 0:width] + 0.5
    w = np.minimum(np.minimum(x, width - x) / (width / 2), np.minimum(y, height - y) / (height / 2))
    w = np.clip(w * 1.6 - 0.3, 0, 1)[..., None]
    w = w * w * (3 - 2 * w)
    a = a * w + b * (1 - w)
    lum = a.mean(-1, keepdims=True)
    a = (a - lum) + mean + (lum - lum.mean()) * (spread / max(lum.std(), 1e-6))
    a = a * np.array(tint)
    out = np.concatenate([np.clip(a, 0, 255), np.full((height, width, 1), 255.0)], -1)
    return np.rint(out).astype(np.uint8)


def ui():
    """Yields (sheet, x, y, suffix, image) for the interface patterns, and the grain for flat boxes as image files."""
    # SP299 32x16 at (288, 120): the stone behind the sidebar, status panel and dialogs; light enough for black text
    yield 'SP299', 288, 120, '', tile('stone', 32 * S, 16 * S, 0.17, 150, 22)
    # SP299 16x16 at (208, 100): the city screen background; dark for its light and colored text
    yield 'SP299', 208, 100, '', tile('slate', 16 * S, 16 * S, 0.25, 52, 8, (0.95, 1.0, 1.1))


def grain():
    """The painted grain for flat boxes: mid grey (128 keeps a box's color), low contrast, repeating without seams."""
    g = tile('grain', 256, 256, 0.5, 124, 9)
    return np.repeat(np.rint(g[..., :3].mean(-1, keepdims=True)).astype(np.uint8), 3, -1)


def main(out):
    count = 0
    for sheet, x, y, suffix, img in list(sprites()) + list(ui()):
        folder = os.path.join(out, sheet)
        os.makedirs(folder, exist_ok=True)
        if not (y >= 160 or suffix):
            # only units and the city are meant to carry key colors
            img = c.avoid_keys(img.copy())
        c.save(img, os.path.join(folder, f'{x}_{y}{suffix}.png'))
        count += 1
    with open(os.path.join(out, 'scale.txt'), 'w') as f:
        f.write('4\n')
    # text and other pixel art without HD versions are enlarged with Scale4x
    with open(os.path.join(out, 'upscale.txt'), 'w') as f:
        f.write('scale2x\n')
    os.makedirs(os.path.join(out, 'ui'), exist_ok=True)
    Image.fromarray(grain(), 'RGB').save(os.path.join(out, 'ui', 'material.png'))
    print(f'{count} sprites written to {out}')


if __name__ == '__main__':
    if len(sys.argv) > 2:
        OBJECTS = sys.argv[2]
    PRINT = print_style(OBJECTS)
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, '..', '..', 'assets', 'hd4'))
