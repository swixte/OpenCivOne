"""Renders a sample map from a folder of generated HD region files (2x or 4x), using the game's tile
choices, so what the game will load can be checked without the game.

Usage: python3 preview_files.py HD_FOLDER OUT.png
"""

import os
import re
import sys
import numpy as np
from PIL import Image

import common as c
from preview import SCENE, PLAYER_COLORS, with_player, DIGITS

DIRS = {1: (0, -1), 2: (1, -1), 3: (1, 0), 4: (1, 1), 5: (0, 1), 6: (-1, 1), 7: (-1, 0), 8: (-1, -1)}
QUARTERS = {0: (0, 0), 1: (8, 0), 2: (8, 8), 3: (0, 8)}
SPECIALS = {0x1c: 224, 0xc1: 240, 0x07: 256, 0x70: 272, 0x8f: 288, 0xf8: 304}
ROWS = {'d': 0, 'p': 1, 'g': 2, 'f': 3, 'h': 4, 'm': 5, 't': 6, 'a': 7, 's': 8, 'j': 9}


class Art:
    def __init__(self, folder):
        self.scale = 2
        scale_file = os.path.join(folder, 'scale.txt')
        if os.path.exists(scale_file):
            self.scale = int(open(scale_file).read().strip())
        self.sprites = {}
        for sheet in os.listdir(folder):
            path = os.path.join(folder, sheet)
            if not os.path.isdir(path):
                continue
            for f in os.listdir(path):
                m = re.match(r'^(\d+)_(\d+)(?:_t(\d+))?', f)
                if m:
                    img = np.asarray(Image.open(os.path.join(path, f)).convert('RGBA'))
                    self.sprites[(sheet, int(m.group(1)), int(m.group(2)))] = (img, int(m.group(3) or 0) * self.scale)

    def get(self, sheet, x, y):
        return self.sprites.get((sheet, x, y), (None, 0))


def render(art, rows, units, cities, roads, rails, resources):
    S = art.scale
    tile = 16 * S
    H, W = len(rows), len(rows[0])
    out = np.zeros((H * tile, W * tile, 4), dtype=np.uint8)

    def at(x, y):
        return rows[y][x % W] if 0 <= y < H else None

    def draw(img, px, py, above=0):
        """img's bottom part at pixel (px, py); `above` rows reach higher."""
        if img is None:
            return
        py -= above
        h, w = img.shape[:2]
        sy, sx = max(0, -py), max(0, -px)
        y0, x0 = max(0, py), max(0, px)
        region = out[y0:y0 + h - sy, x0:x0 + w - sx]
        out[y0:y0 + region.shape[0], x0:x0 + region.shape[1]] = c.over(region, img[sy:sy + region.shape[0], sx:sx + region.shape[1]])

    for y in range(H):
        for x in range(W):
            code = rows[y][x]
            px, py = x * tile, y * tile
            if code == 'o':
                mask = 0
                for i in range(1, 9):
                    mask >>= 1
                    n = at(x + DIRS[i][0], y + DIRS[i][1])
                    if n is not None and n != 'o':
                        mask |= 0x80
                bm = ((mask >> 6) & 3) + (mask << 2)
                for q in range(4):
                    col, _ = art.get('TER257', ((bm >> (q * 2)) & 7) * 16, 176)
                    qx, qy = QUARTERS[q]
                    out[py + qy * S:py + (qy + 8) * S, px + qx * S:px + (qx + 8) * S] = col[qy * S:(qy + 8) * S, qx * S:(qx + 8) * S]
                if mask in SPECIALS:
                    draw(art.get('SP299', SPECIALS[mask], 100)[0], px, py)
                for i, d in enumerate((1, 3, 5, 7)):
                    if at(x + DIRS[d][0], y + DIRS[d][1]) == 'r':
                        draw(art.get('TER257', 128 + i * 16, 176)[0], px, py)
            else:
                draw(art.get('SP299', 256, 120)[0], px, py)
                if code == 'r':
                    mask = 0
                    for i in (1, 3, 5, 7):
                        mask >>= 1
                        if at(x + DIRS[i][0], y + DIRS[i][1]) in ('o', 'r'):
                            mask |= 8
                    draw(art.get('SP257', mask * 16, 64)[0], px, py)
                else:
                    mask = 0
                    for i in (1, 3, 5, 7):
                        mask >>= 1
                        if at(x + DIRS[i][0], y + DIRS[i][1]) == code:
                            mask |= 8
                    row = ROWS[code]
                    img, above = art.get('TER257', (mask if row < 6 else 0) * 16, row * 16)
                    draw(img, px, py, above)
            for d in range(1, 9):
                n = (x + DIRS[d][0], y + DIRS[d][1])
                if (x, y) in roads and n in roads:
                    both = (x, y) in rails and n in rails
                    draw(art.get('SP257', (128 if both else 0) + (d - 1) * 16, 96 if both else 48)[0], px, py)
            if (x, y) in resources:
                draw(art.get('SP257', resources[(x, y)] * 16 + 1, 113)[0], px, py)
            if (x, y) in cities:
                size, player, walls = cities[(x, y)]
                main, second = PLAYER_COLORS[player]
                out[py + S:py + 14 * S, px + S:px + 14 * S] = (255, 255, 255, 255)
                out[py + S:py + 13 * S, px + 2 * S:px + 14 * S] = second + (255,)
                out[py + 2 * S:py + 14 * S, px + 2 * S:px + 14 * S] = main + (255,)
                img, above = art.get('SP257', 12 * 16 + 1, 113)
                draw(with_player(img, player), px + S, py + S, above)
                for n, ch in enumerate(str(size)):
                    bits = DIGITS[ch]
                    x0 = (6 if size < 10 else 3)
                    for b in range(15):
                        if bits[b] == '1':
                            qx, qy = px + (x0 + n * 4 + b % 3) * S, py + (5 + b // 3) * S
                            out[qy:qy + S, qx:qx + S] = (0, 0, 0, 255)
                if walls:
                    draw(art.get('SP257', 13 * 16 + 1, 113)[0], px + S, py + S)
    for kind, player, x, y in units:
        img, _ = art.get('SP257', (kind % 20) * 16 + 1, (kind // 20) * 16 + 161)
        draw(with_player(img, player), x * tile, y * tile)
    return out


if __name__ == '__main__':
    art = Art(sys.argv[1])
    roads = {(5, 6), (6, 6), (7, 6), (8, 6), (9, 6), (4, 6), (4, 7), (4, 8), (16, 5), (16, 6), (17, 6), (15, 4), (16, 4)}
    rails = {(8, 6), (9, 6), (9, 5), (16, 5), (16, 6), (17, 6)}
    cities = {(5, 6): (8, 0, True), (9, 6): (3, 0, False), (4, 8): (5, 0, False), (17, 6): (6, 1, True), (15, 4): (2, 1, False)}
    resources = {(10, 4): 5, (3, 5): 1, (4, 9): 0, (11, 8): 9, (12, 5): 10, (7, 3): 3, (6, 1): 7, (8, 4): 4}
    units = [(0, 0, 6, 7), (2, 0, 5, 5), (7, 0, 7, 8), (11, 0, 8, 7), (9, 0, 3, 8), (17, 0, 12, 4), (19, 0, 2, 3),
             (14, 0, 11, 10), (3, 1, 16, 7), (6, 1, 18, 5), (5, 1, 15, 6), (21, 1, 17, 8), (23, 1, 19, 4), (16, 1, 14, 3),
             (27, 1, 18, 2), (12, 1, 15, 5)]
    img = render(art, SCENE, units, cities, roads, rails, resources)
    Image.fromarray(img).save(sys.argv[2])
    print(sys.argv[2], img.shape, 'scale', art.scale)
