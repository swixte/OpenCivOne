"""Paints the ground textures whose look must say which Civilization terrain a tile is, seen from straight above:
hills as rolling mounds lit from the top left, swamp as murky ground with pools of dark water and reeds, tundra as
grey-brown moss with frost. Seamless, 512x512.

Usage: python3 terrain_signatures.py GROUND_DIR [--seed 1]
"""

import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ridge_texture import gradient, periodic_noise  # noqa: E402

N = 512


def lit(h, strength):
    gx, gy = gradient(h)
    gx, gy = gx * strength, gy * strength
    nz = 1 / np.sqrt(gx ** 2 + gy ** 2 + 1)
    return np.clip((gx * 0.6 + gy * 0.6 + 0.5) * nz, 0, 1)


def save(col, path):
    Image.fromarray(col.clip(0, 255).astype(np.uint8)).save(path)


def hills(rng):
    # rolling mounds about a third of a cell across, clearly lit and shaded
    h = periodic_noise(rng, N, 22) * 0.7 + periodic_noise(rng, N, 11) * 0.5 + periodic_noise(rng, N, 60) * 0.08
    shade = lit(h, 90)
    base = np.array([146, 130, 74], np.float32)
    col = base[None, None] * (0.55 + 0.75 * shade)[..., None]
    # greener in the hollows, drier and paler on the crests
    hollow = np.clip(-h * 0.6, 0, 1)[..., None]
    col = col * (1 - 0.35 * hollow) + np.array([86, 104, 52], np.float32) * (0.6 + 0.6 * shade)[..., None] * 0.35 * hollow
    col *= (1 + periodic_noise(rng, N, 140) * 0.03)[..., None]
    return col


def swamp(rng):
    base = periodic_noise(rng, N, 12)
    col = np.array([76, 86, 50], np.float32)[None, None] * (1 + base * 0.1)[..., None]
    # pools of dark still water, edged with paler reeds
    pools = periodic_noise(rng, N, 18) + periodic_noise(rng, N, 40) * 0.4
    water = np.clip((pools - 0.7) / 0.25, 0, 1)
    rim = np.clip(1 - np.abs(pools - 0.62) / 0.12, 0, 1) * (1 - water)
    col = col * (1 - water[..., None]) + np.array([34, 58, 62], np.float32) * water[..., None]
    col = col * (1 - 0.5 * rim[..., None]) + np.array([150, 150, 92], np.float32) * 0.5 * rim[..., None]
    # reed tufts
    tuft = np.clip((periodic_noise(rng, N, 150) - 1.4) / 0.6, 0, 1) * (1 - water)
    col = col * (1 - tuft[..., None]) + np.array([120, 128, 62], np.float32) * tuft[..., None]
    return col


def tundra(rng):
    h = periodic_noise(rng, N, 16) * 0.6 + periodic_noise(rng, N, 50) * 0.4
    shade = lit(h, 40)
    col = np.array([132, 124, 104], np.float32)[None, None] * (0.7 + 0.45 * shade)[..., None]
    moss = np.clip((periodic_noise(rng, N, 24) - 0.2) / 0.8, 0, 1)[..., None]
    col = col * (1 - 0.45 * moss) + np.array([104, 112, 72], np.float32) * 0.45 * moss
    frost = np.clip((periodic_noise(rng, N, 90) - 1.0) / 0.6, 0, 1)[..., None]
    col = col * (1 - 0.7 * frost) + np.array([226, 232, 236], np.float32) * 0.7 * frost
    return col


def main():
    args = sys.argv[1:]
    seed = 1
    if '--seed' in args:
        i = args.index('--seed'); seed = int(args[i + 1]); del args[i:i + 2]
    if len(args) != 1:
        sys.exit(__doc__)
    g = args[0]
    save(hills(np.random.default_rng(seed)), os.path.join(g, 'hills.png'))
    save(swamp(np.random.default_rng(seed + 1)), os.path.join(g, 'swamp.png'))
    save(tundra(np.random.default_rng(seed + 2)), os.path.join(g, 'tundra.png'))
    print('hills, swamp, tundra written to', g)


if __name__ == '__main__':
    main()
