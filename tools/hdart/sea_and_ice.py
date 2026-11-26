"""Paints the fish resource as a school of fish seen from above through clear water (no disc), and the snow ground
(arctic ice caps) as a clean white-blue ice sheet with pressure ridges and cracks, lit from the top left.

Usage: python3 sea_and_ice.py PAINTED_DIR [--seed 1]
  writes PAINTED_DIR/objects/res_fish.png and PAINTED_DIR/ground/snow.png
"""

import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ridge_texture import gradient, periodic_noise  # noqa: E402


def fish(rng, size=160):
    big = size * 3
    img = Image.new('RGBA', (big, big), (0, 0, 0, 0))
    shade = Image.new('L', (big, big), 0)
    d, sd = ImageDraw.Draw(img), ImageDraw.Draw(shade)
    # a faint pale swirl of disturbed water under the school
    for k in range(30):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0.15, 0.38) * big
        x, y = big / 2 + math.cos(a) * r, big / 2 + math.sin(a) * r
        w = rng.uniform(10, 26)
        d.arc([x - w, y - w * 0.4, x + w, y + w * 0.4], 200, 340, fill=(200, 236, 236, 90), width=3)
    heading = rng.uniform(0, 2 * math.pi)
    for _ in range(9):
        a = rng.uniform(0, 2 * math.pi)
        r = math.sqrt(rng.uniform(0, 1)) * big * 0.3
        x, y = big / 2 + math.cos(a) * r, big / 2 + math.sin(a) * r
        h = heading + rng.normal(0, 0.35)
        L, Wd = rng.uniform(34, 48), rng.uniform(10, 14)
        ca, sa = math.cos(h), math.sin(h)
        def P(u, v):
            return (x + ca * u - sa * v, y + sa * u + ca * v)
        body = [P(L * 0.55, 0), P(L * 0.2, Wd * 0.5), P(-L * 0.3, Wd * 0.32), P(-L * 0.45, 0), P(-L * 0.3, -Wd * 0.32), P(L * 0.2, -Wd * 0.5)]
        tail = [P(-L * 0.42, 0), P(-L * 0.7, Wd * 0.45), P(-L * 0.62, 0), P(-L * 0.7, -Wd * 0.45)]
        sd.polygon([(px + 8, py + 10) for px, py in body + tail], fill=120)
        d.polygon(body, fill=(34, 62, 72, 235))
        d.polygon(tail, fill=(34, 62, 72, 235))
        # the back catches the light
        d.line([P(L * 0.4, -Wd * 0.08), P(-L * 0.25, -Wd * 0.08)], fill=(150, 196, 200, 220), width=4)
    sh = Image.new('RGBA', (big, big), (8, 30, 40, 0))
    sh.putalpha(shade.filter(ImageFilter.GaussianBlur(5)))
    out = Image.alpha_composite(sh, img)
    return out.resize((size, size), Image.LANCZOS)


def ice(rng, n=512):
    base = periodic_noise(rng, n, 3) * 0.6 + periodic_noise(rng, n, 9) * 0.3 + periodic_noise(rng, n, 30) * 0.1
    ridges = 1 - np.abs(np.tanh(periodic_noise(rng, n, 6) * 2.2))
    h = base * 0.5 + ridges ** 6 * 0.5
    gx, gy = gradient(h)
    gx, gy = gx * n * 0.25, gy * n * 0.25
    nz = 1 / np.sqrt(gx ** 2 + gy ** 2 + 1)
    shade = np.clip((gx * 0.6 + gy * 0.6 + 0.6) * nz, 0, 1)
    col = np.array([214, 228, 238], np.float32)[None, None] * (0.78 + 0.3 * shade)[..., None]
    # blue in the hollows and shadows, as packed snow and ice look
    col += ((1 - shade) * 30)[..., None] * np.array([-1, -0.3, 0.5], np.float32)
    # thin dark-blue cracks between floes
    crack = np.clip((0.04 - np.abs(periodic_noise(rng, n, 7))) / 0.04, 0, 1) ** 2
    col = col * (1 - 0.45 * crack[..., None]) + np.array([120, 160, 190], np.float32) * 0.45 * crack[..., None]
    col *= (1 + periodic_noise(rng, n, 120) * 0.02)[..., None]
    return Image.fromarray(col.clip(0, 255).astype(np.uint8))


def main():
    args = sys.argv[1:]
    seed = 1
    if '--seed' in args:
        i = args.index('--seed'); seed = int(args[i + 1]); del args[i:i + 2]
    if len(args) != 1:
        sys.exit(__doc__)
    folder = args[0]
    fish(np.random.default_rng(seed)).save(os.path.join(folder, 'objects', 'res_fish.png'))
    ice(np.random.default_rng(seed + 1)).save(os.path.join(folder, 'ground', 'snow.png'))
    print('fish and ice written to', folder)


if __name__ == '__main__':
    main()
