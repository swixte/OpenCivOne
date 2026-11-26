"""Paints a seamless forest canopy as it looks from straight above (as in satellite views): crowns packed so tight they
merge into one surface, cool blue-green, each crown lit on its upper-left side, with dark shadowy gaps winding between
clumps of trees. No ground shows through.

Usage: python3 canopy_texture.py OUT.png [--size 512] [--seed 1] [--crown 7] [--palette forest|jungle]
"""

import sys

import numpy as np
from PIL import Image

PALETTES = {
    # shadow, mid, highlight (sampled from satellite views of temperate forest)
    'forest': ((14, 38, 46), (40, 84, 74), (100, 146, 112)),
    'jungle': ((14, 42, 30), (44, 92, 52), (118, 160, 84)),
}


def periodic_noise(rng, n, freq):
    f = np.fft.fftfreq(n) * n
    fx, fy = np.meshgrid(f, f)
    r = np.sqrt(fx ** 2 + fy ** 2)
    band = np.exp(-((np.log((r + 1e-6) / freq)) ** 2) / 0.3)
    out = np.real(np.fft.ifft2(np.fft.fft2(rng.normal(size=(n, n))) * band))
    return (out - out.mean()) / (out.std() + 1e-9)


def main():
    args = sys.argv[1:]
    opts = {'--size': '512', '--seed': '1', '--crown': '7', '--palette': 'forest'}
    for key in list(opts):
        if key in args:
            i = args.index(key); opts[key] = args[i + 1]; del args[i:i + 2]
    if len(args) != 1:
        sys.exit(__doc__)
    n, rng, cr = int(opts['--size']), np.random.default_rng(int(opts['--seed'])), float(opts['--crown'])
    dark, mid, light = (np.array(c, np.float32) for c in PALETTES[opts['--palette']])

    # the canopy's height: a dome per crown, crowns packed on a jittered grid, the tallest wins
    height = np.zeros((n, n), np.float32)
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float32)
    step = cr * 1.45
    cells = int(n / step)
    for j in range(cells):
        for i in range(cells):
            cx = (i + 0.5 + rng.uniform(-0.4, 0.4)) * n / cells
            cy = (j + 0.5 + rng.uniform(-0.4, 0.4)) * n / cells
            r = cr * rng.uniform(0.6, 1.6)
            tall = rng.uniform(0.7, 1.0)
            x0, x1, y0, y1 = int(cx - r - 1), int(cx + r + 2), int(cy - r - 1), int(cy + r + 2)
            yy, xx = np.mgrid[y0:y1, x0:x1]
            d2 = ((xx - cx) ** 2 + (yy - cy) ** 2) / (r * r)
            dome = np.where(d2 < 1, tall * np.sqrt(np.clip(1 - d2, 0, 1)), 0).astype(np.float32)
            Y, X = yy % n, xx % n
            height[Y, X] = np.maximum(height[Y, X], dome)

    # leafy texture on the crowns
    height += periodic_noise(rng, n, n / (cr * 0.5)) * 0.025

    # winding gaps: low places between clumps of trees, deep in shadow
    gaps = periodic_noise(rng, n, n / (cr * 9)) + periodic_noise(rng, n, n / (cr * 4)) * 0.5
    gap = np.clip((gaps - 0.6) / 1.1, 0, 1)
    height = height * (1 - 0.5 * gap)

    # lit from the top left
    gy, gx = np.gradient(np.pad(height, 1, mode='wrap'))
    gx, gy = gx[1:-1, 1:-1] * cr * 1.6, gy[1:-1, 1:-1] * cr * 1.6
    nz = 1 / np.sqrt(gx ** 2 + gy ** 2 + 1)
    lit = np.clip((gx * 0.6 + gy * 0.6 + 0.5) * nz, 0, 1)
    # low crowns and gaps darker (ambient occlusion)
    tone = np.clip((lit * 1.15) * (0.18 + 0.82 * np.clip(height / 0.85, 0, 1)) ** 1.2, 0, 1)

    t = tone[..., None]
    col = np.where(t < 0.5, dark + (mid - dark) * (t / 0.5), mid + (light - mid) * ((t - 0.5) / 0.5))
    # gentle variation between stands of trees
    col *= (1 + periodic_noise(rng, n, n / (cr * 14)) * 0.07)[..., None]
    # the gaps fall into deep blue shadow
    col = col * (1 - 0.45 * gap[..., None])
    Image.fromarray(col.clip(0, 255).astype(np.uint8)).save(args[0])
    print('saved', args[0])


if __name__ == '__main__':
    main()
