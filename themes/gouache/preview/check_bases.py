"""Checks each unit's placement on its base: draws every unit at 4x with the base's centre (white cross), the point the
unit is placed by (yellow dot: a man's feet, a cart's wheels, a ship's hull) and the base's outline, and prints how far
apart they are and how much base shows on each side of the figure. Usage: python check_bases.py ENGINE_PNG OUT_PNG"""
import sys
import numpy as np
from PIL import Image, ImageDraw
out = sys.argv[2]
sys.argv = [sys.argv[0], sys.argv[1], 'check-scene-']
import compose
from compose import CELL, OBJ, BASE_SQUASH

NAMES = ['settlers', 'militia', 'phalanx', 'legion', 'knights', 'chariot', 'catapult', 'trireme', 'sail']
Z = 4
row = Image.new('RGBA', (CELL * len(NAMES) * Z, CELL * Z), (110, 140, 60, 255))
d = ImageDraw.Draw(row)
c = CELL * Z
for i, n in enumerate(NAMES):
    u, (ax, ay), half = compose.body(OBJ + f'unit_{n}.png', c, n)
    r = min(max(compose.BASE_R.get(n, 0.3), half / c + 0.05), 0.47)
    sq = compose.UNIT_SQUASH.get(n, BASE_SQUASH)
    cx, cy = i * c + c / 2, c * (0.6 if n in compose.SHIPS else 0.7)
    rx, ry = r * c, r * c * sq
    b = compose.unit_base(compose.BLUE, c, False, r=r, squash=sq)
    row.alpha_composite(b, (round(cx - b.width / 2), round(cy - b.height / 2)))
    left, top = round(cx - ax), round(cy - ay)
    row.alpha_composite(u, (left, top))
    # how much base shows left, right, behind and in front of the figure's solid body
    a = np.asarray(u.getchannel('A')) > 128
    ys, xs = np.nonzero(a)
    fx0, fx1 = left + np.percentile(xs, 2), left + np.percentile(xs, 98)
    fy1 = top + np.percentile(ys, 99)
    d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], outline=(255, 0, 255), width=2)
    d.line([cx - 10, cy, cx + 10, cy], fill='white', width=3)
    d.line([cx, cy - 10, cx, cy + 10], fill='white', width=3)
    px, py = left + ax, top + ay
    d.ellipse([px - 5, py - 5, px + 5, py + 5], fill='yellow', outline='black')
    print(f'{n:9s} contact-vs-base offset ({px - cx:+.1f}, {py - cy:+.1f}) px at 4x | base past body: '
          f'left {fx0 - (cx - rx):+5.1f}  right {(cx + rx) - fx1:+5.1f}  front {(cy + ry) - fy1:+5.1f}')
row.convert('RGB').save(out)
