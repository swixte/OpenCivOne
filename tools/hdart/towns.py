"""Installs town pictures for a theme: iso/towns/STYLE/a{age}_s{step}_{w|o}.png.

Each picture is cut from its magenta sheet and scaled to its size step's width, at one scale for all towns (320 pixels
covers 1.25 map cells): size 1-3 at 70%, 4-7 at 92%, 8-12 at 110%, 13 and up at 135%. Colours are
strengthened a little (the generated towns come out paler than the rest of the map's art). 'w' has City Walls, 'o' is open.

Usage: python3 towns.py THEME_HD_DIR STYLE AGE RAW_DIR NAME_STEP0 NAME_STEP1 NAME_STEP2 NAME_STEP3
       (each NAME is the raw file's name without _w/_o; RAW_DIR/NAME_w.png and RAW_DIR/NAME_o.png)
"""
import os
import sys

import numpy as np
from PIL import Image, ImageEnhance

sys.path.insert(0, os.path.dirname(__file__))
from painted_assets import cut_object  # noqa: E402

WIDTHS = (0.7, 0.92, 1.1, 1.35)


def main(hd, style, age, raw, names):
    out = os.path.join(hd, 'iso', 'towns', style)
    os.makedirs(out, exist_ok=True)
    for step, name in enumerate(names):
        for w in ('w', 'o'):
            src = os.path.join(raw, f'{name}_{w}.png')
            if not os.path.exists(src):
                continue
            img = cut_object(Image.open(src), size=2048)
            # pink fringe left from the magenta background (a slightly off-magenta sheet) is cleared
            a = np.asarray(img).copy()
            r, g, b = a[..., 0].astype(int), a[..., 1].astype(int), a[..., 2].astype(int)
            pink = (r > 140) & (b > 90) & (g < np.minimum(r, b) * 0.72)
            a[..., 3][pink] = 0
            # and pinkish ground (magenta bled into the painting) turned back to tan earth, except where pink is
            # meant (the South Asian sandstone)
            if style != 'sa':
                tint = (r > 150) & (r > g + 35) & (b > g - 15)
                a[..., 2][tint] = (g[tint] * 0.8).astype(np.uint8)
            img = Image.fromarray(a, 'RGBA')
            width = int(320 * WIDTHS[step])
            img = img.resize((width, int(img.height * width / img.width)), Image.LANCZOS)
            alpha = img.getchannel('A')
            rgb = ImageEnhance.Contrast(ImageEnhance.Color(img.convert('RGB')).enhance(1.12)).enhance(1.12)
            rgb = ImageEnhance.Brightness(rgb).enhance(0.98)
            img = rgb.convert('RGBA')
            img.putalpha(alpha)
            img.save(os.path.join(out, f'a{age}_s{step}_{w}.png'))
    print(f'{style} age {age}: {len(names)} sizes written')


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4], sys.argv[5:9])
