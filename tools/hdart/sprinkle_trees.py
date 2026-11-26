"""Sprinkles small tree crowns seen from straight above over a ground texture, as open land looks from high up.

Each crown is a small dome of the forest's leaves, lit from the top left, with a soft shadow to the lower right. The
texture repeats, so crowns wrap around its edges and the result stays seamless.

Usage: python3 sprinkle_trees.py GROUND_DIR NAME COUNT [--leaves forest] [--size 3.2,5.0] [--seed 1] [--out PATH] [--tone 1]
  --tone      the crowns' brightness (below 1 for dark trees that stand out on light meadow)
  GROUND_DIR  a theme's hd/painted/ground folder
  NAME        the texture to sprinkle (grass, plains, ...)
  COUNT       how many crowns over the whole texture (it covers 8x8 cells)
"""

import sys

import numpy as np
from PIL import Image


def main():
    args = sys.argv[1:]
    opts = {'--leaves': 'forest', '--size': '3.2,5.0', '--seed': '1', '--out': None, '--tone': '1'}
    for key in list(opts):
        if key in args:
            i = args.index(key)
            opts[key] = args[i + 1]
            del args[i:i + 2]
    if len(args) != 3:
        sys.exit(__doc__)
    folder, name, count = args[0], args[1], int(args[2])
    lo, hi = (float(v) for v in opts['--size'].split(','))
    rng = np.random.default_rng(int(opts['--seed']))
    img = np.asarray(Image.open(f'{folder}/{name}.png').convert('RGB'), np.float32).copy()
    leaves = np.asarray(Image.open(f'{folder}/{opts["--leaves"]}.png').convert('RGB'), np.float32)
    h, w = img.shape[:2]
    lh, lw = leaves.shape[:2]
    # a leaf colour a little lighter and warmer than the deep forest, as lone trees in sun are
    leaf_mean = leaves.reshape(-1, 3).mean(0)

    # clumped placement: most crowns gather near seeds, the rest are scattered
    seeds = rng.uniform(0, [w, h], (max(1, count // 12), 2))
    pts = []
    for _ in range(count):
        if rng.random() < 0.6:
            s = seeds[rng.integers(len(seeds))]
            pts.append(s + rng.normal(0, 14, 2))
        else:
            pts.append(rng.uniform(0, [w, h]))

    def stamp(cx, cy, r, fn):
        x0, x1 = int(np.floor(cx - r * 1.6)), int(np.ceil(cx + r * 1.6))
        y0, y1 = int(np.floor(cy - r * 1.6)), int(np.ceil(cy + r * 1.6))
        ys, xs = np.mgrid[y0:y1 + 1, x0:x1 + 1]
        fn(ys % h, xs % w, (xs - cx) / r, (ys - cy) / r)

    crowns = [(p[0] % w, p[1] % h, rng.uniform(lo, hi), rng.uniform(-0.12, 0.12)) for p in pts]
    for cx, cy, r, _ in crowns:
        def shadow(yy, xx, ux, uy):
            d = np.sqrt((ux - 0.45) ** 2 + (uy - 0.55) ** 2)
            dark = 0.3 * np.clip((1.15 - d) / 0.5, 0, 1)
            img[yy, xx] *= (1 - dark[..., None] * np.array([1, 0.9, 0.75], np.float32))
        stamp(cx, cy, r, shadow)
    for cx, cy, r, shade in crowns:
        def crown(yy, xx, ux, uy):
            d = np.sqrt(ux ** 2 + uy ** 2)
            jitter = rng.uniform(-0.12, 0.12, d.shape)
            cover = np.clip((1.0 + jitter - d) / 0.25, 0, 1)
            z = np.sqrt(np.clip(1 - d ** 2, 0, 1))
            lit = np.clip(-ux * 0.55 - uy * 0.6 + z * 0.58, 0, 1)
            tone = (0.7 + 0.75 * lit) * (1 + shade) * float(opts['--tone'])
            leaf = leaves[yy % lh, xx % lw] * 0.6 + leaf_mean * 0.4
            col = leaf * tone[..., None] * np.array([1.05, 1.08, 0.9], np.float32)
            img[yy, xx] += (col - img[yy, xx]) * cover[..., None]
        stamp(cx, cy, r, crown)

    Image.fromarray(img.clip(0, 255).astype(np.uint8)).save(opts['--out'] or f'{folder}/{name}.png')
    print(f'{name}: {count} crowns')


if __name__ == '__main__':
    main()
