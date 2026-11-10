"""Builds a theme's isometric map art (hd/iso) from its generated isometric sheets (raw-iso/iso_*.png): every sheet cut
into separate sprites, one folder per set, and the inked grass and sand grounds made into seamless 400-pixel textures
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

SETS = ['trees', 'peaks', 'hills2', 'jungle', 'town', 'tufts', 'tundra', 'reeds', 'ice', 'dunes', 'plots', 'hamlet']
GROUNDS = {'grass': ('iso_tex_grass', (1.0, 1.0, 1.0)), 'sand': ('iso_tex_sand', (1.0, 0.94, 0.80))}
TEXTURE = 400


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
    for name in SETS:
        folder = os.path.join(out, 'hills' if name == 'hills2' else name)
        os.makedirs(folder, exist_ok=True)
        sprites = cut_sprites(Image.open(os.path.join(raw, f'iso_{name}.png')))
        for i, spr in enumerate(sprites):
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


if __name__ == '__main__':
    main(sys.argv[1])
