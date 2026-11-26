"""Builds the palace garden pictures for a theme from generated art (see themes/<theme>/garden/prompts.json).

CBACKS1-3: the forecourt below the palace (the bottom 65 rows of the 320x200 screen), made from the theme's CBACK
backdrop with a path of earth/gravel, stone or marble laid into it in perspective, running from the palace to the
viewer. CBRUSH0-5: the garden plantings in the bottom corners, left (even) and right (odd, mirrored), for the three
levels: flower beds, hedges, then trees and a lantern. Everything at 4x the stand-in sizes.

Usage: python3 palace_garden.py RAW_DIR THEME_HD_DIR
"""
import sys

import numpy as np
from PIL import Image, ImageFilter

sys.path.insert(0, __import__('os').path.dirname(__file__))
from painted_assets import cut_object  # noqa: E402

S = 4
TOP = 135 * S  # where the forecourt strip starts


def path_strip(back, texture, near=400, far=110, centre=640):
    """the backdrop's bottom strip with a perspective path from the palace foot (far) to the bottom edge (near)"""
    strip = np.asarray(back.crop((0, TOP, 1280, 800)).convert('RGB')).astype(np.float32)
    tex = np.asarray(texture.convert('RGB').resize((512, 512), Image.LANCZOS)).astype(np.float32)
    h = strip.shape[0]
    out = strip.copy()
    edge_ink = np.array([46, 38, 30], np.float32)
    for y in range(h):
        t = y / (h - 1)
        half = (far + (near - far) * t) / 2
        z = 1 / (0.25 + 0.75 * t)          # depth: rows near the palace are further away
        v = int((z * 300) % 512)
        for x in range(int(centre - half - 3), int(centre + half + 4)):
            if x < 0 or x >= 1280:
                continue
            d = abs(x - centre) - half      # distance outside the path's edge (negative inside)
            if d > 3:
                continue
            u = int(((x - centre) * z * 1.1 + 4096) % 512)
            c = tex[v, u]
            # light falls off a little towards the far end, as in the backdrop's haze
            # toned to the print: warmer, softer contrast, and a little hazier towards the far end
            grey = c.mean()
            c = (grey + (c - grey) * 0.8) * np.array([0.9, 0.86, 0.74]) + np.array([14, 12, 8])
            c = c * (0.84 + 0.16 * t) + np.array([30, 26, 16]) * (1 - t) * 0.5
            if d > 0:          # the inked edge, a thin dark key line
                a = 1 - d / 3
                out[y, x] = out[y, x] * (1 - a * 0.85) + edge_ink * a * 0.85
            elif d > -2:
                out[y, x] = edge_ink * 0.6 + c * 0.4
            else:
                out[y, x] = c
    # the far end fades into the ground at the palace's foot
    img = Image.fromarray(np.clip(out, 0, 255).astype(np.uint8))
    blend = np.asarray(img).astype(np.float32)
    for y in range(0, 28):
        a = y / 28
        blend[y] = strip[y] * (1 - a) + blend[y] * a
    return Image.fromarray(np.clip(blend, 0, 255).astype(np.uint8))


def planting(raw, size, fill=0.92, inner=True):
    """a planting cut from its magenta sheet and set in the bottom of a transparent picture of the given size"""
    obj = cut_object(raw, size=1024)
    w, h = size
    k = min(w * fill / obj.width, h * 0.95 / obj.height)
    obj = obj.resize((max(1, int(obj.width * k)), max(1, int(obj.height * k))), Image.LANCZOS)
    canvas = Image.new('RGBA', size, (0, 0, 0, 0))
    x = (w - obj.width) // 2 - (int(w * 0.04) if inner else 0)
    canvas.alpha_composite(obj, (max(0, x), h - obj.height - 6))
    return canvas


def main(raw_dir, hd):
    back = Image.open(f'{hd}/CBACK.jpg')
    for level, name in ((1, 'path1'), (2, 'path2'), (3, 'path3')):
        path_strip(back, Image.open(f'{raw_dir}/{name}.png')).save(f'{hd}/CBACKS{level}.jpg', quality=92)
    for level, name in ((1, 'garden1'), (2, 'garden2'), (3, 'garden3')):
        size = (136 * S, (95 if level == 1 else 106) * S)
        left = planting(Image.open(f'{raw_dir}/{name}.png'), size)
        left.save(f'{hd}/CBRUSH{(level - 1) * 2}.png')
        left.transpose(Image.FLIP_LEFT_RIGHT).save(f'{hd}/CBRUSH{(level - 1) * 2 + 1}.png')
    print('palace garden written')


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
