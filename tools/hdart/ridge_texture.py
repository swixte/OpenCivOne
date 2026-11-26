"""Paints a seamless mountain texture seen from straight above: ranges of sharp crests with spurs and gullies running
down their flanks (as erosion carves them), lit from the top left, snow on the high crests. Procedural, so it needs no
image service.

The crests come from ridged noise; the spurs are layers of gullies, each a sum of short wave patches across a periodic
grid, every patch's waves running down the slope under it, so ridges branch off the crests the way real ranges do.

Usage: python3 ridge_texture.py OUT.png [--size 512] [--seed 1] [--snow 0.68] [--rock R,G,B] [--ranges 2]
"""

import sys

import numpy as np
from PIL import Image


def periodic_noise(rng, n, freq):
    """Seamless smooth noise with its energy around `freq` cycles across the tile."""
    f = np.fft.fftfreq(n) * n
    fx, fy = np.meshgrid(f, f)
    r = np.sqrt(fx ** 2 + fy ** 2)
    band = np.exp(-((np.log((r + 1e-6) / freq)) ** 2) / 0.3)
    out = np.real(np.fft.ifft2(np.fft.fft2(rng.normal(size=(n, n))) * band))
    return (out - out.mean()) / (out.std() + 1e-9)


def gradient(h):
    gy, gx = np.gradient(np.pad(h, 1, mode='wrap'))
    return gx[1:-1, 1:-1], gy[1:-1, 1:-1]


def gullies(height, spacing, freq, sharp=1.0):
    """Waves running down the slope of `height`, blended over a periodic grid of patches (seamless)."""
    n = height.shape[0]
    gx, gy = gradient(height)
    mag = np.sqrt(gx ** 2 + gy ** 2) + 1e-9
    dx, dy = gx / mag, gy / mag                      # downhill/uphill direction
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float32)
    total = np.zeros((n, n), np.float32)
    wsum = np.zeros((n, n), np.float32)
    cells = n // spacing
    ci, cj = np.floor(xs / spacing), np.floor(ys / spacing)
    for oj in (-1, 0, 1, 2):
        for oi in (-1, 0, 1, 2):
            cx, cy = (ci + oi + 0.5) * spacing, (cj + oj + 0.5) * spacing
            px, py = xs - cx, ys - cy
            w = np.exp(-(px ** 2 + py ** 2) / (2 * (spacing * 0.6) ** 2))
            # phase across the slope: waves whose crests run downhill
            across = px * (-dy) + py * dx
            phase = (((ci + oi) % cells) * 12.9898 + ((cj + oj) % cells) * 78.233) % 6.283
            # a sharp-crested wave: spurs with knife edges and rounded gullies between them
            total += w * (1 - 2 * np.abs(np.sin((across * freq * np.pi / spacing) + phase)))
            wsum += w
    wave = total / wsum
    return np.sign(wave) * np.abs(wave) ** sharp


def main():
    args = sys.argv[1:]
    opts = {'--size': '512', '--seed': '1', '--snow': '0.68', '--rock': '128,124,118', '--ranges': '2'}
    for key in list(opts):
        if key in args:
            i = args.index(key)
            opts[key] = args[i + 1]
            del args[i:i + 2]
    if len(args) != 1:
        sys.exit(__doc__)
    n, rng = int(opts['--size']), np.random.default_rng(int(opts['--seed']))
    rock = np.array([float(v) for v in opts['--rock'].split(',')], np.float32)
    ranges = float(opts['--ranges'])

    # the ranges: ridged noise, crests where the smooth noise crosses zero
    base = periodic_noise(rng, n, ranges) * 0.8 + periodic_noise(rng, n, ranges * 2) * 0.35
    height = 1 - np.abs(np.tanh(base * 0.55))
    height = height ** 1.15

    # spurs and gullies down the flanks, finer layers weaker; they fade out on the crest and in the valleys
    flank = np.clip(height * (1.1 - height) * 4.5, 0, 1)
    for spacing, freq, amp in ((48, 1.5, 0.22), (24, 1.5, 0.11), (12, 1.4, 0.05)):
        height = height + gullies(height, spacing, freq, 0.8) * amp * (0.35 + 0.65 * flank)
    height += periodic_noise(rng, n, 64) * 0.012
    height = (height - height.min()) / (height.max() - height.min())

    # light from the top left
    gx, gy = gradient(height)
    gx, gy = gx * n * 0.55, gy * n * 0.55
    nz = 1 / np.sqrt(gx ** 2 + gy ** 2 + 1)
    shade = np.clip((gx * 0.6 + gy * 0.6 + 0.52) * nz, 0, 1)
    tone = 0.5 + 0.8 * shade

    col = rock[None, None] * tone[..., None]
    # cool blue-grey in the shadowed faces, warm on the lit ones
    col += ((1 - shade) * 8)[..., None] * np.array([-0.5, 0, 1], np.float32)
    col += (shade * 10)[..., None] * np.array([1, 0.6, -0.4], np.float32)
    # low ground: scrub and scree, greener and darker
    low = np.clip((0.3 - height) / 0.3, 0, 1)[..., None]
    col = col * (1 - 0.55 * low) + np.array([88, 96, 58], np.float32) * tone[..., None] * 0.55 * low
    # snow in streaks on the high crests, brightest where lit
    streak = periodic_noise(rng, n, 40)
    snow = np.clip((height + streak * 0.05 - float(opts['--snow'])) / 0.1, 0, 1)
    col = col * (1 - snow[..., None]) + (np.array([214, 222, 232], np.float32) * (0.62 + 0.45 * shade)[..., None]) * snow[..., None]
    col *= (1 + periodic_noise(rng, n, 120) * 0.035)[..., None]
    Image.fromarray(col.clip(0, 255).astype(np.uint8)).save(args[0])
    print('saved', args[0])


if __name__ == '__main__':
    main()
