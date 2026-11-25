"""The value and saturation test of the aesthetic check: does the map read in grey, and does nothing shout in colour?

Takes screenshots of the map (the real game's, e.g. from LIVE_SCRIPT's shot:), crops the map window, and
  - writes them side by side in grey, for judging the value structure by eye: sea, open land and forest in distinct
    bands of darkness, towns readable by their dark masses, nothing a white flare;
  - measures the saturation (max - min channel) and brightness, and fails a shot whose saturation is over the caps.

Usage: python3 valuecheck.py OUT.jpg SHOT.png [SHOT.png ...] [--crop X,Y,W,H]
  --crop  the map window in the shots (default 320,32,960,768: the 1280x800 game window's map)

Caps (a muted palette, the gouache-atlas direction): median saturation at most 60, 95th percentile at most 110,
99th at most 140. Exits with 1 if any shot is over them.
"""

import sys

import numpy as np
from PIL import Image, ImageDraw

CAPS = {50: 60, 95: 110, 99: 140}
LUMA = np.array([0.299, 0.587, 0.114], np.float32)


def main():
    args = sys.argv[1:]
    crop = (320, 32, 960, 768)
    if '--crop' in args:
        i = args.index('--crop')
        crop = tuple(int(v) for v in args[i + 1].split(','))
        del args[i:i + 2]
    if len(args) < 2:
        sys.exit(__doc__)
    out, shots = args[0], args[1:]
    x, y, w, h = crop
    tiles, failed = [], False
    for path in shots:
        im = Image.open(path).convert('RGB').crop((x, y, x + w, y + h))
        a = np.asarray(im, np.float32)
        sat = a.max(-1) - a.min(-1)
        val = a @ LUMA
        s = {p: float(np.percentile(sat, p)) for p in CAPS}
        over = [f'p{p} {s[p]:.0f} > {c}' for p, c in CAPS.items() if s[p] > c]
        failed |= bool(over)
        v = np.percentile(val, [5, 50, 95])
        line = (f'{path}: saturation p50 {s[50]:.0f} p95 {s[95]:.0f} p99 {s[99]:.0f}; value p5 {v[0]:.0f} p50 {v[1]:.0f} '
                f'p95 {v[2]:.0f}; {"FAIL " + ", ".join(over) if over else "pass"}')
        print(line)
        tiles.append((im.convert('L').convert('RGB'), path.rsplit('/', 1)[-1], bool(over)))
    sheet = Image.new('RGB', (len(tiles) * (w + 10), h + 22), (30, 30, 30))
    draw = ImageDraw.Draw(sheet)
    for i, (tile, name, bad) in enumerate(tiles):
        sheet.paste(tile, (i * (w + 10), 22))
        draw.text((i * (w + 10) + 4, 4), name + (' (over the saturation caps)' if bad else ''), fill=(230, 120, 120) if bad else (230, 230, 230))
    sheet.save(out, quality=90)
    sys.exit(1 if failed else 0)


if __name__ == '__main__':
    main()
