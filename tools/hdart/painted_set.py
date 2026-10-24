"""Writes the 4x sprite set that goes with the painted map: units, resources, cities, walls, fortress and huts from the
painted objects (assets/painted/objects, see painted_assets.py), and the remaining map sprites (roads, railroads,
irrigation, mines, pollution, fog, explosions, shield bonus) from the drawn 2x set at twice the size.

Terrain tiles are left out: the painted map replaces them. The output has scale.txt with 4.

Usage: python3 painted_set.py [OUTPUT_DIR] [OBJECTS_DIR]     (defaults: assets/hd4 and assets/painted/objects)
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


def obj(name):
    return np.asarray(Image.open(os.path.join(OBJECTS, name + '.png')).convert('RGBA'))


def up2(img):
    """Drawn 2x art at 4x."""
    h, w = img.shape[:2]
    return np.asarray(Image.fromarray(img).resize((w * 2, h * 2), Image.LANCZOS))


def icon(name, size, bottom=None, shadow=True):
    """A painted object fitted into a size x size sprite, standing on its bottom edge, with a soft shadow."""
    img = np.zeros((UNIT, UNIT, 4), dtype=np.uint8)
    fitted = style.fit(obj(name), width=size, height=size)
    bottom = UNIT - 2 if bottom is None else bottom
    if shadow:
        style.shadow(img, UNIT / 2, bottom - 1, fitted.shape[1] * 0.42, alpha=90)
    style.paste(img, fitted, UNIT / 2, bottom)
    return img


def unit(j):
    """Unit j in UnitTypeEnum order, with a small flag in the player's colors (key colors) at the top left."""
    img = np.zeros((UNIT, UNIT, 4), dtype=np.uint8)
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
    yield 'SP257', 12 * 16 + 1, 113, f'_t{CITY_UP // S}_k0-5', city()
    yield 'SP257', 13 * 16 + 1, 113, '', icon('walls', UNIT, UNIT - 1, shadow=False)
    yield 'SP257', 14 * 16 + 1, 113, '', icon('fortress', UNIT - 6)
    yield 'SP257', 15 * 16 + 1, 113, '', icon('hut', 44)
    yield 'SP257', 153, 41, '', up2(o.shield_bonus())
    for i, direction in enumerate((1, 3, 5, 7)):
        yield 'SP257', 80 + i * 16, 128, '', up2(o.fog(direction))
    for i in range(8):
        yield 'SP257', i * 16 + 1, 97, '', up2(o.explosion(i))
    for j in range(len(u.UNIT_NAMES)):
        yield 'SP257', (j % 20) * 16 + 1, (j // 20) * 16 + 161, '', unit(j)


def main(out):
    count = 0
    for sheet, x, y, suffix, img in sprites():
        folder = os.path.join(out, sheet)
        os.makedirs(folder, exist_ok=True)
        if not (y >= 160 or suffix):
            # only units and the city are meant to carry key colors
            img = c.avoid_keys(img.copy())
        c.save(img, os.path.join(folder, f'{x}_{y}{suffix}.png'))
        count += 1
    with open(os.path.join(out, 'scale.txt'), 'w') as f:
        f.write('4\n')
    print(f'{count} sprites written to {out}')


if __name__ == '__main__':
    if len(sys.argv) > 2:
        OBJECTS = sys.argv[2]
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, '..', '..', 'assets', 'hd4'))
