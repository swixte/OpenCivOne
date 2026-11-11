"""Builds a theme's isometric map art (hd/iso) from its generated isometric sheets (raw-iso/iso_*.png): every sheet cut
into separate sprites, one folder per set, the painted crop plots cut from their grid, and the inked grass and sand grounds made into seamless 400-pixel textures
(400 divides the isometric width of the world, so the ground repeats without a seam where the world wraps).

Usage: python3 iso_assets.py THEME_DIR
"""

import colorsys
import os
import shutil
import sys

import numpy as np
from PIL import Image

from painted_assets import cut_sprites
import style

SETS = ['trees', 'broadleaf', 'peaks', 'hills2', 'hills3', 'jungle', 'town', 'tufts', 'tundra', 'reeds', 'ice', 'dunes', 'plots', 'hamlet']
GROUNDS = {'grass': ('iso_tex_grass', (1.0, 1.0, 1.0)), 'sand': ('iso_tex_sand', (1.0, 0.94, 0.80))}
TEXTURE = 400
PLOT = 32  # the painted crop plots (iso_fieldplots, a 4 x 4 grid seen from above), mapped onto each field plot


def lighten(img):
    """Field plots lighter and yellow-green, as the mockup's fields, instead of dark green with orange squares."""
    a = np.asarray(img.convert('RGBA')).astype(np.float64)
    rgb = a[..., :3] / 255
    h, s, v = np.vectorize(colorsys.rgb_to_hsv)(rgb[..., 0], rgb[..., 1], rgb[..., 2])
    h = np.where(h < 0.14, h + 0.07, h)
    v = np.clip(v * 1.22 + 0.04, 0, 1)
    r, g, b = np.vectorize(colorsys.hsv_to_rgb)(h, s * 0.9, v)
    a[..., :3] = np.dstack([r, g, b]) * 255
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), 'RGBA')


def main(theme):
    raw = os.path.join(theme, 'raw-iso')
    out = os.path.join(theme, 'hd', 'iso')
    shutil.rmtree(out, ignore_errors=True)
    counts = {}
    for name in SETS:
        # every hills sheet (hills2: rounded mounds, hills3: ridges, double humps, rocky knolls, terraces) goes into one set
        target = 'hills' if name.startswith('hills') else name
        folder = os.path.join(out, target)
        os.makedirs(folder, exist_ok=True)
        sprites = cut_sprites(Image.open(os.path.join(raw, f'iso_{name}.png')))
        first = counts.get(target, 0)
        counts[target] = first + len(sprites)
        for i, spr in enumerate(sprites):
            i += first
            if name == 'plots':
                spr = lighten(spr)
            if max(spr.size) > 320:
                k = 320 / max(spr.size)
                spr = spr.resize((max(1, round(spr.width * k)), max(1, round(spr.height * k))), Image.LANCZOS)
            spr.save(os.path.join(folder, f'{i}.png'))
        print(name, len(sprites))
    os.makedirs(os.path.join(out, 'ground'), exist_ok=True)
    for name, (sheet, tint) in GROUNDS.items():
        img = Image.open(os.path.join(raw, sheet + '.png')).convert('RGB')
        tile = np.asarray(Image.fromarray(style.seamless(img, img.width)).convert('RGB').resize((TEXTURE, TEXTURE), Image.LANCZOS), dtype=np.float64)
        Image.fromarray(np.clip(tile * np.array(tint), 0, 255).astype(np.uint8)).save(os.path.join(out, 'ground', name + '.png'))
    print('grounds', len(GROUNDS))
    plots = os.path.join(raw, 'iso_fieldplots.png')
    if os.path.exists(plots):
        os.makedirs(os.path.join(out, 'fieldplots'), exist_ok=True)
        img = Image.open(plots).convert('RGB')
        cell = img.width / 4
        inset = cell * 0.05
        for i in range(16):
            x, y = (i % 4) * cell, (i // 4) * cell
            img.crop((round(x + inset), round(y + inset), round(x + cell - inset), round(y + cell - inset))).resize(
                (PLOT, PLOT), Image.LANCZOS).save(os.path.join(out, 'fieldplots', f'{i}.png'))
        print('fieldplots', 16)


if __name__ == '__main__':
    main(sys.argv[1])
