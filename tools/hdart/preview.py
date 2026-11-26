"""Renders sample maps from the generated HD sprites, using the same tile choices as the game
(MapManagement.F0_2aea_03ba_DrawCell), so the art can be checked in place without the game files.

Usage: python3 preview.py OUT.png [--scale N] [--scene] [--style PACK_DIR]
"""

import sys
import numpy as np
from PIL import Image
import common as c
import terrain as t

LAND_CODES = {'d': 'desert', 'p': 'plains', 'g': 'grassland', 'f': 'forest', 'h': 'hills', 'm': 'mountains',
              't': 'tundra', 'a': 'arctic', 's': 'swamp', 'j': 'jungle', 'r': 'river'}

SAMPLE = [
    "oooooooooooooooooooooooo",
    "oooaaaaaoooooooooooooooo",
    "ooaattttaoooooooggpppooo",
    "oottffhmmooooooggppfffoo",
    "ootggffmmmooooogprrrffoo",
    "oogggrrrhhoooooggrpgfhoo",
    "ooggrrgggmmooooogrgggmmo",
    "oopprgfffhhooooogrrgmmmo",
    "oopprggffjjjoooooggrggoo",
    "oodppprsssjjjooooooroooo",
    "ooddpprssjjjoooooooooooo",
    "oodddprrsooooooooaoooooo",
    "ooodddrooooooooooooooooo",
    "oooooooooooooooooooooooo",
]


class Atlas:
    """Caches generated tiles; with sprites from generate.py when available."""

    def __init__(self, sprites=None):
        self.cache = {}
        self.sprites = sprites or {}

    def get(self, key, fn):
        if key not in self.cache:
            self.cache[key] = fn()
        return self.cache[key]


def compose(rows, atlas, units=(), cities=(), roads=(), resources=()):
    """Cells are drawn row by row with their own overlays, like the game draws cells; units come last."""
    H, W = len(rows), len(rows[0])
    out = np.zeros((H * c.T, W * c.T, 4), dtype=np.uint8)

    def at(x, y):
        if y < 0 or y >= H:
            return None
        return rows[y][x % W]

    def blit(img, x, y, ox=0, oy=0):
        """Draw img with its tile-sized bottom part at the tile; rows above that reach into the tile above."""
        h, w = img.shape[:2]
        x0 = x * c.T + ox
        y0 = y * c.T - (h - c.T) if (oy == 0 and h > c.T) else y * c.T + oy
        sy = max(0, -y0)
        y0 = max(0, y0)
        part = img[sy:]
        region = out[y0:y0 + part.shape[0], x0:x0 + w]
        out[y0:y0 + region.shape[0], x0:x0 + region.shape[1]] = c.over(region, part[:region.shape[0], :region.shape[1]])

    grass = atlas.get('grass', t.grass_background)
    per_cell = {}
    for draw in (roads, resources, cities):
        for img, x, y, ox, oy in draw:
            per_cell.setdefault((x, y), []).append((img, ox, oy))

    def cell_overlays(x, y):
        for img, ox, oy in per_cell.get((x, y), ()):
            blit(img, x, y, ox, oy)

    for y in range(H):
        for x in range(W):
            code = rows[y][x]
            if code == 'o':
                mask = 0
                for i in range(1, 9):
                    mask >>= 1
                    dx, dy = t.DIRS[i]
                    n = at(x + dx, y + dy)
                    if n is not None and n != 'o':
                        mask |= 0x80
                bitmap_mask = ((mask >> 6) & 3) + (mask << 2)
                for q in range(4):
                    v = (bitmap_mask >> (q * 2)) & 7
                    col = atlas.get(('coast', v), lambda v=v: t.coast_column(v))
                    ox, oy = t.QUARTER_ORIGIN[q]
                    out[y * c.T + oy:y * c.T + oy + 16, x * c.T + ox:x * c.T + ox + 16] = col[oy:oy + 16, ox:ox + 16]
                for sx, m in t.SPECIAL_COASTS:
                    if mask == m:
                        blit(atlas.get(('special', m), lambda m=m: t.special_coast(m)), x, y)
                for d in (1, 3, 5, 7):
                    dx, dy = t.DIRS[d]
                    if at(x + dx, y + dy) == 'r':
                        blit(atlas.get(('delta', d), lambda d=d: t.delta(d)), x, y)
                cell_overlays(x, y)
                continue
            blit(grass, x, y)
            if code == 'r':
                mask = 0
                for i in (1, 3, 5, 7):
                    mask >>= 1
                    dx, dy = t.DIRS[i]
                    if at(x + dx, y + dy) in ('o', 'r'):
                        mask |= 8
                if mask:
                    blit(atlas.get(('river', mask), lambda m=mask: t.river(m)), x, y)
                cell_overlays(x, y)
                continue
            name = LAND_CODES[code]
            mask = 0
            for i in (1, 3, 5, 7):
                mask >>= 1
                dx, dy = t.DIRS[i]
                if at(x + dx, y + dy) == code:
                    mask |= 8
            row = t.TERRAIN_ROWS.index(name)
            if row >= 6:
                mask = 0
            blit(atlas.get(('terrain', name, mask), lambda n=name, m=mask: t.terrain_tile(n, m)), x, y)
            cell_overlays(x, y)
    for img, x, y, ox, oy in units:
        blit(img, x, y, ox, oy)
    return out


# ---------- a populated scene: cities, roads, units, resources, fog ----------

# EGA colors the game uses for players: (main color, second color)
PLAYER_COLORS = [((0xfb, 0x53, 0x53), (0xa7, 0x00, 0x00)), ((0x53, 0x53, 0xfb), (0x00, 0x00, 0xa7)),
                 ((0xfb, 0xfb, 0x53), (0x53, 0xfb, 0x53)), ((0xfb, 0xfb, 0xfb), (0xa7, 0xa7, 0xa7))]


def with_player(img, player):
    """Resolve key colors the way the engine does after the game's player color swap."""
    main, second = PLAYER_COLORS[player]
    out = img.copy()
    r, g, b, a = (img[..., k].astype(np.int64) for k in range(4))
    prim = (a == 255) & (g == 0) & (r == b) & (r > 0)
    sec = (a == 255) & (r == 0) & (g == b) & (g > 0)
    for m, col, shade in ((prim, main, r), (sec, second, g)):
        for k in range(3):
            out[..., k] = np.where(m, np.clip(col[k] * shade / 128, 0, 255), out[..., k])
    return out


DIGITS = {'0': "111101101101111", '1': "010110010010111", '2': "110001010100111", '3': "110001010001110",
          '4': "101101111001001", '5': "111100110001110", '6': "011100111101111", '7': "111001010010010",
          '8': "111101111101111", '9': "111101111001110"}


def city_tile(size, player, walls=False, up=8):
    """A city drawn the way the game does it: borders, player color square, city sprite, size, walls.
    The result has `up` extra rows on top for the part of the city that rises into the tile above."""
    import overlays as o
    main, second = PLAYER_COLORS[player]
    full = np.zeros((c.T + up, c.T, 4), dtype=np.uint8)
    tile = full[up:]
    tile[2:28, 2:28] = (255, 255, 255, 255)
    tile[2:26, 4:28] = second + (255,)
    tile[4:28, 4:28] = main + (255,)
    sprite = with_player(o.city_streets(up), player)
    full[2:2 + sprite.shape[0], 2:32] = c.over(full[2:2 + sprite.shape[0], 2:32], sprite[:, :30])
    text = str(size)
    x0 = (6 if size < 10 else 3) * 2
    for n, ch in enumerate(text):
        bits = DIGITS[ch]
        for b in range(15):
            if bits[b] == '1':
                px, py = x0 + (n * 4 + b % 3) * 2, 10 + (b // 3) * 2
                tile[py:py + 2, px:px + 2] = (0, 0, 0, 255)
    if walls:
        tile[2:32, 2:32] = c.over(tile[2:32, 2:32], o.city_walls())
    return full


SCENE = [
    "oooooooooooooooooooooo",
    "oooooaaaaooooooooooooo",
    "ooootttttaoooooggooooo",
    "ooogttffhmmooogppgoooo",
    "oogggffhhmmmooggpffooo",
    "oogpgrrrhhmooggprfffoo",
    "ooppgrggggmooogprgfjoo",
    "oodppgrfffjoooogrgjjjo",
    "oodddprssjjjooooorjjoo",
    "ooodddrsssjooooooooooo",
    "oooodddrsooooooooooooo",
    "oooooooooooooooooooooo",
]


def scene(atlas):
    import overlays as o
    import units as u
    roads = set([(5, 6), (6, 6), (7, 6), (8, 6), (9, 6), (4, 6), (4, 7), (4, 8)])
    rails = set([(8, 6), (9, 6), (9, 5)])
    cities = {(5, 6): (8, 0, True), (9, 6): (3, 0, False), (4, 8): (5, 0, False), (17, 6): (6, 1, True), (15, 4): (2, 1, False)}
    roads |= {(16, 5), (16, 6), (17, 6), (15, 4), (16, 4)}
    rails |= {(16, 5), (16, 6), (17, 6)}
    overlays = []
    for (x, y) in sorted(roads):
        for d in range(1, 9):
            dx, dy = t.DIRS[d]
            if (x + dx, y + dy) in roads:
                both = (x, y) in rails and (x + dx, y + dy) in rails
                fn = o.railroad if both else o.road
                overlays.append((atlas.get((fn.__name__, d), lambda fn=fn, d=d: fn(d)), x, y, 0, 0))
    for (x, y) in [(6, 7), (7, 7), (3, 7), (3, 6)]:
        overlays.append((atlas.get('irrigation', o.irrigation), x, y, 0, 0))
    overlays.append((atlas.get('mine', o.mine), 8, 5, 0, 0))
    overlays.append((atlas.get('pollution', o.pollution), 10, 6, 0, 0))
    resources = [(o.RESOURCES[5](), 10, 4), (o.RESOURCES[1](), 3, 5), (o.RESOURCES[0](), 4, 9), (o.RESOURCES[9](), 11, 8),
                 (o.RESOURCES[10](), 12, 5), (o.RESOURCES[3](), 7, 3), (o.RESOURCES[7](), 6, 1), (o.RESOURCES[4](), 8, 4)]
    res = [(img, x, y, 0, 0) for img, x, y in resources]
    res.append((o.hut(), 19, 7, 0, 0))
    res.append((o.fortress(), 11, 3, 0, 0))
    res.append((o.shield_bonus(), 2, 6, 8, 8))
    city_imgs = [(city_tile(s, p, w), x, y, 0, 0) for (x, y), (s, p, w) in cities.items()]
    unit_list = [(0, 0, 6, 7), (2, 0, 5, 5), (7, 0, 7, 8), (11, 0, 8, 7), (9, 0, 3, 8), (17, 0, 12, 4), (19, 0, 2, 3),
                 (14, 0, 11, 10), (3, 1, 16, 7), (6, 1, 18, 5), (5, 1, 15, 6), (21, 1, 17, 8), (23, 1, 19, 4), (16, 1, 14, 3),
                 (27, 1, 18, 2), (12, 1, 15, 5)]
    us = []
    for kind, player, x, y in unit_list:
        img = with_player(u.unit(kind), player)
        us.append((img, x, y, 0, 0))
    out = compose(SCENE, atlas, units=us, cities=city_imgs, roads=overlays, resources=res)
    # fog: the bottom right corner is unexplored
    H, W = len(SCENE), len(SCENE[0])
    unexplored = {(x, y) for x in range(W) for y in range(H) if x >= 20 or y >= 11}
    for y in range(H):
        for x in range(W):
            if (x, y) in unexplored:
                out[y * c.T:(y + 1) * c.T, x * c.T:(x + 1) * c.T] = (0, 0, 0, 255)
                continue
            for d in (1, 3, 5, 7):
                dx, dy = t.DIRS[d]
                if (x + dx, y + dy) in unexplored:
                    region = out[y * c.T:(y + 1) * c.T, x * c.T:(x + 1) * c.T]
                    out[y * c.T:(y + 1) * c.T, x * c.T:(x + 1) * c.T] = c.over(region, atlas.get(('fog', d), lambda d=d: o.fog(d)))
    return out


if __name__ == '__main__':
    path = sys.argv[1]
    if '--style' in sys.argv:
        import style
        style.load(sys.argv[sys.argv.index('--style') + 1])
    scale = int(sys.argv[sys.argv.index('--scale') + 1]) if '--scale' in sys.argv else 2
    img = scene(Atlas()) if '--scene' in sys.argv else compose(SAMPLE, Atlas())
    Image.fromarray(img).resize((img.shape[1] * scale, img.shape[0] * scale), Image.NEAREST).save(path)
    print(path, img.shape)
