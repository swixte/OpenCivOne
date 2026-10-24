"""Terrain art: grass background, terrain types with their 16 neighbour variants, ocean, coasts and rivers.

How the game uses these (MapManagement.F0_2aea_03ba_DrawCell):
- every land tile starts with the grass background, then its terrain sprite is drawn over it;
- terrain sprites have 16 variants, picked by which of the N, E, S, W neighbours have the same terrain
  (bit 0 = N, bit 1 = E, bit 2 = S, bit 3 = W); where a side is not shared the terrain fades out
  and the grass background shows, which blends different terrains;
- ocean tiles are made of four 8x8 quarters, each chosen by three neighbours (see coast_column);
- rivers are drawn over the grass background, connected to river and ocean neighbours.
"""

import numpy as np
from common import (T, grid, fbm, vnoise, hash2, ramp, pick, blank, over, bayer)
import style


def from_pack(name):
    """Use the style pack's texture for `name` when it has one, otherwise the procedural one."""
    def wrap(fn):
        def inner(*args, **kwargs):
            tex = style.texture(name, T)
            return tex.copy() if tex is not None else fn(*args, **kwargs)
        return inner
    return wrap

# ---------- palettes (dark to light) ----------
R = dict(
    grass=ramp('#1d4419', '#275a1f', '#327026', '#3f862d', '#509b36', '#69b143', '#8ac65a'),
    meadow=ramp('#2a5a1c', '#367024', '#43872c', '#549d34', '#6ab33f', '#86c64f', '#a5d86a'),
    forest_floor=ramp('#12301a', '#183f1f', '#1f4f25', '#295f2b', '#347232'),
    plains=ramp('#4d4a1f', '#686327', '#837c30', '#9e963b', '#b7ae4b', '#cdc463', '#e0d885'),
    desert=ramp('#7c5826', '#9a7232', '#b78c41', '#cfa553', '#e1bd6c', '#eed28c', '#f7e6b4'),
    tundra=ramp('#353b33', '#4a5245', '#5f6857', '#757e69', '#8d957e', '#a8ad97', '#c4c7b4'),
    arctic=ramp('#5b77a0', '#7893bb', '#98b1d2', '#b8cce4', '#d3e1f0', '#e9f1f9', '#fbfdff'),
    swamp=ramp('#1b2a17', '#26391d', '#334a24', '#415c2b', '#516f34', '#66843f'),
    swamp_water=ramp('#102a30', '#173b42', '#215056', '#2f666a', '#4a8582', '#7fb0a8'),
    jungle_floor=ramp('#0f2e12', '#153d17', '#1d4f1d', '#266224', '#33772c'),
    hills_floor=ramp('#3a3b1c', '#4e5024', '#64672d', '#7b7f37', '#939743', '#acaf55'),
    hill=ramp('#353f19', '#475520', '#5b6b28', '#718231', '#88993d', '#a0af4f', '#bac56a'),
    rock=ramp('#34302b', '#47413a', '#5b544b', '#70685d', '#867d70', '#9d9485', '#b5ad9e'),
    rock_lit=ramp('#6e665c', '#857c70', '#9b9284', '#b1a899', '#c7bfb1'),
    rock_shade=ramp('#2f2a26', '#3d3731', '#4b443d', '#5a5249'),
    snow_lit=ramp('#c9d6e6', '#e1eaf4', '#f6f9fc'),
    snow_shade=ramp('#7f93b3', '#97aac6', '#b0c0d6'),
    tree=ramp('#0b2111', '#113017', '#18421e', '#215626', '#2d6c2f', '#3f853a', '#589f47'),
    jungle=ramp('#0b3010', '#104416', '#175b1c', '#1f7322', '#2b8c2a', '#40a636', '#62bf48'),
    water=ramp('#0b1c45', '#112a60', '#183b7c', '#205095', '#2a66ab', '#3580bd', '#4699c8', '#5fb2d1', '#80c9d8'),
    sand=ramp('#8a6a3a', '#a5824a', '#c09c5e', '#d6b676', '#e6cc93', '#f1dcae'),
    river=ramp('#1d4f8f', '#2a68ab', '#3a84c4', '#5aa3d6', '#8cc4e6'),
    mud=ramp('#3a2e1c', '#4c3d25', '#5e4c2f'),
)
FOAM = np.array([230, 244, 246, 255], dtype=np.uint8)
FOAM2 = np.array([159, 211, 223, 255], dtype=np.uint8)
TRUNK = np.array([90, 58, 28, 255], dtype=np.uint8)
TRUNK_D = np.array([54, 35, 15, 255], dtype=np.uint8)
OUTLINE = np.array([33, 28, 24, 255], dtype=np.uint8)
L3 = np.array([-0.55, -0.7, 0.55]) / np.linalg.norm([-0.55, -0.7, 0.55])

X, Y = grid()


def _img_from(colors, alpha=None):
    img = blank()
    img[...] = colors
    if alpha is not None:
        img[..., 3] = np.where(alpha, 255, 0)
    return img


# ---------- grass background ----------

@from_pack('grass')
def grass_background():
    h = lambda x, y: fbm(x, y, 11) + fbm(x, y, 12, cells=(4, 2)) * 0.3
    v = 0.5 + (fbm(X, Y, 13, cells=(32, 16)) - 0.5) * 0.7 + (h(X + 1, Y + 1) - h(X - 1, Y - 1)) * 2.2
    tuft = hash2(X, Y, 14) < 0.05
    below = hash2(X, Y - 1, 14) < 0.05
    v = v + np.where(tuft, 0.3, 0) - np.where(~tuft & below, 0.25, 0)
    return _img_from(pick(R['grass'], v, X, Y))


# ---------- coverage of a terrain sprite ----------

SIDES = {'N': 0, 'E': 1, 'S': 2, 'W': 3}


def coverage(mask, seed, margin=5.0, wobble=2.5, corner=7.0):
    """Signed distance-like field: > 0 where the terrain covers the tile.

    Sides whose neighbour has the same terrain (bit set in `mask`) are not limited; other sides
    pull back by `margin` pixels with a wobbly, rounded edge.
    """
    dist = {'N': Y + 0.5, 'S': T - 0.5 - Y, 'W': X + 0.5, 'E': T - 0.5 - X}
    along = {'N': X, 'S': X, 'W': Y, 'E': Y}
    f = np.full(X.shape, 99.0)
    edges = {}
    for side, bit in SIDES.items():
        if mask & (1 << bit):
            continue
        w = (vnoise(along[side], np.full(X.shape, bit * 7.0), 8, seed + bit) - 0.5) * 2 * wobble
        edges[side] = dist[side] - margin - w
        f = np.minimum(f, edges[side])
    for a, b in (('N', 'E'), ('E', 'S'), ('S', 'W'), ('W', 'N')):
        if a in edges and b in edges:
            ea, eb = edges[a], edges[b]
            rounded = corner - np.hypot(np.maximum(corner - ea, 0), np.maximum(corner - eb, 0))
            f = np.minimum(f, np.where((ea < corner) & (eb < corner), rounded, f))
    return f


def apply_coverage(colors, f, rim_dark=None, rim_light=None):
    """Cut a full-tile texture to its coverage, with a darker rim on the bottom right edge of the shape."""
    img = _img_from(colors)
    inside = f > 0
    img[~inside, 3] = 0
    if rim_dark is not None:
        edge = inside & (f < 1.2)
        # light comes from the top left: edges facing down or right are in shade
        lower_right = (np.roll(f, -1, 0) < f) | (np.roll(f, -1, 1) < f)
        img[edge & lower_right] = rim_dark
        if rim_light is not None:
            img[edge & ~lower_right] = rim_light
    return img


# ---------- terrain materials (full tile, tileable) ----------

@from_pack('desert')
def desert_texture():
    def h(x, y):
        return np.sin(2 * np.pi * (2 * x + y) / T + fbm(x, y, 31, cells=(16, 8)) * 9) * 0.5
    v = 0.58 + (h(X + 1, Y + 1) - h(X - 1, Y - 1)) * 0.55 + (fbm(X, Y, 34, cells=(32, 16)) - 0.5) * 0.3 + (fbm(X, Y, 32, cells=(4, 2)) - 0.5) * 0.15
    v = v - np.where(hash2(X, Y, 33) < 0.01, 0.35, 0)
    return pick(R['desert'], v, X, Y)


@from_pack('plains')
def plains_texture():
    h = lambda x, y: fbm(x, y, 22) + fbm(x, y, 23, cells=(4, 2)) * 0.25
    v = 0.5 + (fbm(X, Y, 21, cells=(32, 16)) - 0.5) * 0.6 + (h(X + 1, Y + 1) - h(X - 1, Y - 1)) * 2.2
    stroke = hash2(X // 2, Y, 24) < 0.08
    v = v + np.where(stroke, 0.22, 0) - np.where(~stroke & (hash2(X // 2, Y - 1, 24) < 0.08), 0.18, 0)
    return pick(R['plains'], v, X, Y)


@from_pack('grassland')
def meadow_texture():
    h = lambda x, y: fbm(x, y, 41) + fbm(x, y, 42, cells=(4, 2)) * 0.3
    v = 0.5 + (fbm(X, Y, 43, cells=(32, 16)) - 0.5) * 0.5 + (h(X + 1, Y + 1) - h(X - 1, Y - 1)) * 2.0
    flower = hash2(X, Y, 44) < 0.012
    img = pick(R['meadow'], v, X, Y).copy()
    img[flower] = (236, 224, 120, 255)
    return img


@from_pack('rock')
def rock_texture(seed=74):
    h = lambda x, y: fbm(x, y, seed + 1, cells=(8, 4, 2))
    v = 0.45 + (fbm(X, Y, seed, cells=(16, 8)) - 0.5) * 0.6 + (h(X + 1, Y + 1) - h(X - 1, Y - 1)) * 2.4
    v = v - np.where(hash2(X, Y, seed + 2) < 0.05, 0.3, 0)
    return pick(R['rock'], v, X, Y)


@from_pack('hills_floor')
def hills_floor_texture():
    h = lambda x, y: fbm(x, y, 72) + fbm(x, y, 73, cells=(4, 2)) * 0.3
    v = 0.5 + (fbm(X, Y, 71, cells=(32, 16)) - 0.5) * 0.6 + (h(X + 1, Y + 1) - h(X - 1, Y - 1)) * 2.4
    return pick(R['hills_floor'], v, X, Y)


@from_pack('forest_floor')
def forest_floor_texture():
    v = 0.5 + (fbm(X, Y, 81, cells=(16, 8, 4)) - 0.5) * 0.8
    return pick(R['forest_floor'], v, X, Y)


# ---------- features ----------

def _tall(img, up):
    """Add `up` transparent rows above a tile, for art that reaches into the tile above."""
    if up == 0:
        return img
    return np.concatenate([np.zeros((up, T, 4), dtype=np.uint8), img], 0)


def _inside(img, xx, yy):
    return 0 <= xx < img.shape[1] and 0 <= yy < img.shape[0]


def _tree(img, cx, base, height, half_w, seed, oy=0):
    """A conifer in three tiers, lit from the left, with a dark edge; tile coordinates, drawn `oy` rows lower."""
    top = base - height
    for ty in range(int(round(top)), int(round(base)) - 1):
        yy = ty + oy
        rel = (ty - top) / max(height - 2, 1)
        tier = min(2, int(rel * 3))
        tt = rel * 3 - tier
        w = half_w * (0.3 + 0.22 * tier + 0.48 * tt * (0.6 + 0.2 * tier))
        for xx in range(int(np.floor(cx - w)), int(np.ceil(cx + w)) + 1):
            if not _inside(img, xx, yy):
                continue
            dx = (xx - cx) / max(w, 0.5)
            if abs(dx) > 1:
                continue
            v = 0.55 - dx * 0.35 + (float(hash2(xx, ty, seed)) - 0.5) * 0.3 - rel * 0.15
            if tt < 0.14 and tier > 0:
                v -= 0.25
            if abs(dx) > 0.82 or ty == int(round(top)):
                v = 0
            img[yy, xx] = pick(R['tree'], v, xx, ty)
    c = int(round(cx))
    for ty in range(int(round(base)) - 1, int(round(base)) + 1):
        if _inside(img, c - 1, ty + oy):
            img[ty + oy, c - 1] = TRUNK
        if _inside(img, c, ty + oy):
            img[ty + oy, c] = TRUNK_D


def forest(mask, up=0):
    f = coverage(mask, 300, margin=3.5)
    img = _tall(apply_coverage(forest_floor_texture(), f), up)
    # the top row of trees is taller when it may reach into the tile above
    rows = [10, 17, 24, 31]
    trees = []
    for ri, by in enumerate(rows):
        for bx in range(5 if ri % 2 == 0 else 9, 28, 8):
            jx = (float(hash2(bx, by, 301)) - 0.5) * 3
            jy = (float(hash2(bx, by, 302)) - 0.5) * 2
            cx, base = bx + jx, min(by + jy, 31)
            ix, iy = int(np.clip(round(cx), 0, 31)), int(np.clip(round(base) - 3, 0, 31))
            if f[iy, ix] < 2.5:
                continue
            h = 9 + float(hash2(bx, by, 303)) * 3 + (min(up, 8) * 0.9 if ri == 0 else up * 0.25)
            trees.append((cx, base, h, 4.2 + float(hash2(bx, by, 304)) * 1.2 + (0.6 if ri == 0 and up else 0)))
    tree = style.sprite('tree')
    for i, (cx, base, h, w) in enumerate(sorted(trees, key=lambda t: t[1])):
        if tree is not None:
            spr = style.fit(tree, height=h * 1.25)
            style.shadow(img, cx + 1, base + up, spr.shape[1] * 0.4, alpha=60)
            style.paste(img, spr, cx, base + up + 0.5)
        else:
            _tree(img, cx, base, h, w, 305, oy=up)
    return img


def _dome(img, cx, cy, rx, ry, palette, seed, cut=0.4, oy=0):
    for ty in range(int(np.floor(cy - ry)), int(np.ceil(cy + ry * cut)) + 1):
        for xx in range(int(np.floor(cx - rx)), int(np.ceil(cx + rx)) + 1):
            yy = ty + oy
            if not _inside(img, xx, yy):
                continue
            nx, ny = (xx - cx) / rx, (ty - cy) / ry
            e = nx * nx + ny * ny
            if e > 1 or ty > cy + ry * cut:
                continue
            nz = np.sqrt(1 - e)
            v = 0.12 + (nx * L3[0] + ny * L3[1] + nz * L3[2]) * 0.85 + (float(fbm(np.array(xx), np.array(ty), seed, cells=(4, 2))) - 0.5) * 0.25
            if float(hash2(xx, ty, seed + 1)) < 0.06:
                v += 0.15
            if e > 0.8 and nx + ny * 0.6 > 0:
                v = min(v, 0.05)
            elif e > 0.86:
                v = max(v, 0.85)
            img[yy, xx] = pick(palette, v, xx, ty)


def hills(mask, up=0):
    f = coverage(mask, 400, margin=3.0)
    img = _tall(apply_coverage(hills_floor_texture(), f, rim_dark=R['hills_floor'][0]), up)
    # with room above, the back hills rise into the tile above
    lift = min(up, 6)
    domes = [(10, 12 - lift * 0.6, 9, 8 + lift * 0.5), (23, 14 - lift * 0.4, 9, 7 + lift * 0.4), (15, 24, 11, 7)]
    extra = {0: (16, 5, 8, 5), 1: (27, 22, 6, 6), 2: (24, 28, 8, 4), 3: (5, 22, 6, 6)}
    for bit, d in extra.items():
        if mask & (1 << bit):
            domes.append(d)
    hill = style.sprite('hill')
    for cx, cy, rx, ry in sorted(domes, key=lambda d: d[1]):
        if f[int(np.clip(cy, 0, 31)), int(np.clip(cx, 0, 31))] > 3:
            if hill is not None:
                style.paste(img, style.fit(hill, width=rx * 2.1, height=ry * 1.6), cx, cy + ry * 0.4 + up)
            else:
                _dome(img, cx, cy, rx, ry, R['hill'], 410 + cx, oy=up)
    return img


def _peak(img, own, ax, ay, k, base_y, seed, pid, oy=0):
    for xx in range(T):
        top = ay + abs(xx - ax) * k + (float(fbm(np.array(xx * 1.0), np.array(seed * 0.1 + pid), seed + 7, cells=(4, 2))) - 0.5) * 3
        bottom = base_y - float(fbm(np.array(xx * 1.0), np.array(pid * 3.0), seed + 8, cells=(8, 4))) * 3
        for ty in range(max(-oy, int(np.ceil(top))), min(T, int(bottom) + 1)):
            yy = ty + oy
            ridge = ax + (ty - ay) * 0.28 + (float(fbm(np.array(ty * 1.0), np.array(pid * 5.0), seed + 9, cells=(4, 2))) - 0.5) * 2
            lit = xx < ridge
            tex = float(fbm(np.array(xx * 1.0), np.array(ty * 1.0), seed + 10, cells=(8, 4, 2)))
            snow_line = ay + 6 + (base_y - ay) * 0.12 + (float(fbm(np.array(xx * 1.0), np.array(pid * 9.0), seed + 11, cells=(4, 2))) - 0.5) * 4
            if ty < snow_line:
                c = pick(R['snow_lit'], 0.45 + tex * 0.6 - (0 if xx < ridge - 1 else 0.2), xx, ty) if lit else pick(R['snow_shade'], 0.3 + tex * 0.6, xx, ty)
            else:
                g = (ty - ay) / max(base_y - ay, 1)
                c = pick(R['rock_lit'], 0.85 - g * 0.5 + (tex - 0.5) * 0.7, xx, ty) if lit else pick(R['rock_shade'], 0.7 - g * 0.4 + (tex - 0.5) * 0.7, xx, ty)
                if float(hash2(xx, ty, seed + 12)) < 0.04:
                    c = R['rock_shade'][0]
            if ty - top < 1.2 and own[yy, xx] not in (0, pid):
                c = OUTLINE
            img[yy, xx] = c
            own[yy, xx] = pid
    # outline above and beside the peak
    filled = own == pid
    o = (np.roll(filled, -1, 0) | np.roll(filled, 1, 1) | np.roll(filled, -1, 1)) & (own == 0)
    o[:, 0] = o[:, -1] = False
    o[base_y - 3 + oy:, :] = False
    img[o] = OUTLINE


def mountains(mask, up=0):
    f = coverage(mask, 500, margin=2.5)
    img = _tall(apply_coverage(rock_texture(), f, rim_dark=R['rock'][0]), up)
    own = np.zeros(img.shape[:2], dtype=np.int64)
    # side ridges toward connected neighbours, then the main peak, which rises into the tile above when there is room
    side_peaks = {3: (6, 15, 1.5), 1: (26, 15, 1.5), 0: (16, 6, 1.9)}
    mountain = style.sprite('mountain')
    if mountain is not None:
        # side peaks toward connected neighbours, then the main peak rising into the tile above
        for bit, (ax, ay, k) in side_peaks.items():
            if mask & (1 << bit):
                style.paste(img, style.fit(mountain, width=20, height=27 - ay + up * 0.3), ax, 28 + up)
        main = style.fit(mountain, width=34, height=26 + up * 0.9)
        style.shadow(img, 17, 29 + up, 15, alpha=70)
        style.paste(img, main, 16, 30 + up)
        return img
    pid = 1
    for bit, (ax, ay, k) in side_peaks.items():
        if mask & (1 << bit):
            _peak(img, own, ax, ay - up * 0.3, k, 27, 510 + bit, pid, oy=up)
            pid += 1
    apex = 4 - up * 0.85
    _peak(img, own, 16, apex, (29 - apex) / 16.5, 29, 520, pid, oy=up)
    return img


def grassland(mask):
    f = coverage(mask, 200, margin=4.0, wobble=3.0)
    return apply_coverage(meadow_texture(), f)


def desert(mask):
    f = coverage(mask, 100, margin=4.0)
    return apply_coverage(desert_texture(), f, rim_dark=R['desert'][1], rim_light=R['desert'][5])


def plains(mask):
    f = coverage(mask, 150, margin=4.0)
    return apply_coverage(plains_texture(), f, rim_dark=R['plains'][1])


# Terrains without neighbour variants: full tiles that repeat seamlessly.

@from_pack('tundra')
def tundra():
    h = lambda x, y: fbm(x, y, 43, cells=(8, 4)) + fbm(x, y, 44, cells=(4, 2)) * 0.3
    v = 0.52 + (fbm(X, Y, 41, cells=(32, 16)) - 0.5) * 0.6 + (h(X + 1, Y + 1) - h(X - 1, Y - 1)) * 2.0
    moss = fbm(X, Y, 42, cells=(8, 4)) > 0.62
    v = v - np.where(moss, 0.28, 0) + np.where(hash2(X, Y, 45) < 0.025, 0.35, 0)
    img = _img_from(pick(R['tundra'], v, X, Y))
    snow = fbm(X, Y, 46, cells=(16, 8)) > 0.7
    img[snow] = pick(R['snow_lit'], 0.4 + (fbm(X, Y, 47, cells=(4, 2)) - 0.5), X, Y)[snow]
    return img


@from_pack('arctic')
def arctic():
    h = lambda x, y: fbm(x, y, 62, cells=(16, 8)) * 1.2 + fbm(x, y, 63, cells=(4, 2)) * 0.2
    v = 0.62 + (fbm(X, Y, 61, cells=(32, 16)) - 0.5) * 0.4 + (h(X + 1, Y + 1) - h(X - 1, Y - 1)) * 2.8
    crack = np.abs(fbm(X, Y, 64, cells=(16, 8)) - 0.5) < 0.018
    below = np.abs(fbm(X, Y - 1, 64, cells=(16, 8)) - 0.5) < 0.018
    v = np.where(crack, 0.12, v + np.where(below, 0.2, 0))
    return _img_from(pick(R['arctic'], v, X, Y))


@from_pack('swamp')
def swamp():
    p = fbm(X, Y, 51, cells=(32, 16, 8))
    pool = p > 0.56
    v_water = 0.35 + (p - 0.56) * 2.5
    v_water = np.where(fbm(X - 1, Y - 1, 51, cells=(32, 16, 8)) <= 0.56, 0.02, np.where(fbm(X + 1, Y + 1, 51, cells=(32, 16, 8)) <= 0.56, 0.85, v_water))
    v_water = np.where(hash2(X, Y, 54) < 0.015, 1.0, v_water)
    h = lambda x, y: fbm(x, y, 56, cells=(8, 4)) + fbm(x, y, 57, cells=(4, 2)) * 0.3
    v_ground = 0.5 + (fbm(X, Y, 55, cells=(16, 8)) - 0.5) * 0.6 + (h(X + 1, Y + 1) - h(X - 1, Y - 1)) * 2.0 - np.where(p > 0.52, 0.3, 0)
    img = _img_from(np.where(pool[..., None], pick(R['swamp_water'], v_water, X, Y), pick(R['swamp'], v_ground, X, Y)))
    # reeds: short stalks growing up from random roots, outside pools
    for k in range(4):
        root = (hash2(X, Y + k, 53) < 0.02) & ~pool
        col = (163, 173, 85, 255) if k == 3 else (70, 82, 31, 255) if k == 0 else (111, 126, 51, 255)
        img[root] = col
    return img


def jungle(up=0):
    """Dense canopy. Without room above, blobs wrap around so the tile repeats seamlessly; with room above,
    the top row of blobs rises into the tile above and covers the cut-off blobs at the bottom of that tile."""
    gx_, gy_ = grid(T, T + up)
    Xj, Yj = gx_, gy_ - up
    img = np.zeros((T + up, T, 4), dtype=np.uint8)
    floor = Yj >= 0
    floor_tex = style.texture('jungle_floor', T)
    if floor_tex is not None:
        img[up:] = floor_tex
    else:
        img[floor] = pick(R['jungle_floor'], 0.5 + (fbm(Xj, Yj, 91, cells=(8, 4)) - 0.5) * 0.8, Xj, Yj)[floor]
    blobs = []
    for gy in range(4):
        for gx in range(4):
            cx = gx * 8 + 4 + (float(hash2(gx, gy, 92)) - 0.5) * 4 + (4 if gy % 2 else 0)
            cy = gy * 8 + 4 + (float(hash2(gx, gy, 93)) - 0.5) * 3 - (min(up, 6) * 0.5 if gy == 0 else 0)
            blobs.append((cx % T, cy, 5.2 + float(hash2(gx, gy, 94)) * 1.6))
    palm = style.sprite('jungle_tree')
    if palm is not None:
        # trees on the same grid, repeated left and right so the tile joins its neighbours
        for cx, cy, r in sorted(blobs, key=lambda b: b[1]):
            spr = style.fit(palm, height=r * 2.6)
            for ox in (-T, 0, T):
                style.paste(img, spr, cx + ox, cy + r + up)
        return img
    for cx, cy, r in sorted(blobs, key=lambda b: b[1]):
        for ox in (-T, 0, T):
            for oy in ((0,) if up else (-T, 0, T)):
                bx, by = cx + ox, cy + oy
                dx, dy = Xj - bx, Yj - by
                ang = np.arctan2(dy, dx)
                rr = r * (0.82 + fbm(np.cos(ang) * 6 + 40, np.sin(ang) * 6 + 40, 95, cells=(4, 2)) * 0.36)
                d = np.hypot(dx, dy) / rr
                m = (d <= 1) & (Yj < T)
                if not m.any():
                    continue
                nx, ny = dx / rr, dy / rr
                nz = np.sqrt(np.clip(1 - nx * nx - ny * ny, 0, 1))
                v = 0.08 + (nx * L3[0] + ny * L3[1] + nz * L3[2]) * 0.85 + (fbm(Xj, Yj, 96, cells=(4, 2)) - 0.5) * 0.5
                v = np.where(d > 0.88, np.minimum(v, 0.04), v)
                img[m] = pick(R['jungle'], v, Xj, Yj)[m]
    return img


TERRAIN_ROWS = ['desert', 'plains', 'grassland', 'forest', 'hills', 'mountains', 'tundra', 'arctic', 'swamp', 'jungle']
VARIANT_FUNCS = {'desert': desert, 'plains': plains, 'grassland': grassland, 'forest': forest, 'hills': hills, 'mountains': mountains}
SINGLE_FUNCS = {'tundra': tundra, 'arctic': arctic, 'swamp': swamp, 'jungle': jungle}


# HD rows of art above the tile for terrains that rise into the tile above (a multiple of 2)
TALL = {'forest': 8, 'hills': 6, 'mountains': 16, 'jungle': 8}


def terrain_tile(name, mask, tall=True):
    """The terrain sprite; with `tall`, terrains in TALL get extra rows of art above the tile."""
    up = TALL.get(name, 0) if tall else 0
    if name in VARIANT_FUNCS:
        return VARIANT_FUNCS[name](mask, up) if up else VARIANT_FUNCS[name](mask)
    return SINGLE_FUNCS[name](up) if up else SINGLE_FUNCS[name]()


# ---------- ocean and coasts ----------

DIRS = {1: (0, -1), 2: (1, -1), 3: (1, 0), 4: (1, 1), 5: (0, 1), 6: (-1, 1), 7: (-1, 0), 8: (-1, -1)}
# Quarter q (0 top left, 1 top right, 2 bottom right, 3 bottom left) depends on three directions:
# bit 0 the side before it clockwise, bit 1 the corner, bit 2 the side after it.
QUARTER_DIRS = {0: (7, 8, 1), 1: (1, 2, 3), 2: (3, 4, 5), 3: (5, 6, 7)}
QUARTER_ORIGIN = {0: (0, 0), 1: (16, 0), 2: (16, 16), 3: (0, 16)}


def land_distance(land_dirs, x=X, y=Y):
    """Distance from each pixel of a tile to the nearest land neighbour (directions 1..8)."""
    d = np.full(x.shape, 99.0)
    for k in land_dirs:
        dx, dy = DIRS[k]
        # distance to the neighbour tile's square, with the tile edge at -0.5 and 31.5 in pixel centres
        ex = np.where(dx < 0, x + 0.5, np.where(dx > 0, T - 0.5 - x, 0))
        ey = np.where(dy < 0, y + 0.5, np.where(dy > 0, T - 0.5 - y, 0))
        if dx and dy:
            dd = np.hypot(ex, ey)
        else:
            dd = ex + ey
        d = np.minimum(d, dd)
    return d


def ocean_field(land_dirs):
    """A full ocean tile with shallows, foam and beach toward the given land directions."""
    d = land_distance(land_dirs)
    # wobble the shore line with noise that repeats every tile, so neighbouring tiles match
    d = d + (fbm(X, Y, 701, cells=(8, 4)) - 0.5) * 3.0
    h = lambda x, y: fbm(x, y * 2, 702, cells=(16, 8, 4))
    wave = (h(X + 1, Y + 1) - h(X - 1, Y - 1)) * 1.6
    depth = np.clip(1 - d / 13, 0, 1)
    v = 0.22 + depth * 0.6 + wave * 0.3 + (fbm(X, Y, 703, cells=(32, 16)) - 0.5) * 0.12
    water = style.texture('ocean', T)
    if water is not None:
        # the pack's water, lighter and more turquoise toward the shore
        rgb = water[..., :3].astype(np.float64) * (0.8 + depth[..., None] * 0.5)
        rgb = rgb * (1 - depth[..., None] * 0.3) + np.array([90.0, 190.0, 200.0]) * depth[..., None] * 0.3
        img = _img_from(np.concatenate([np.clip(rgb, 0, 255), np.full((T, T, 1), 255.0)], -1).astype(np.uint8))
    else:
        img = _img_from(pick(R['water'], v, X, Y, dither=0.55))
        glint = (hash2(X, Y, 704) < 0.004) & (depth < 0.3)
        img[glint] = R['water'][7]
    foam2 = (d > 3.4) & (d < 4.2) & (fbm(X, Y, 705, cells=(8, 4)) > 0.62)
    img[foam2] = FOAM2
    foam = (d <= 2.6) & (d > 1.6) & ((bayer(X, Y) + (fbm(X, Y, 706, cells=(4, 2)) - 0.5)) > 0.05)
    img[foam] = FOAM
    beach = d <= 1.6
    sand = style.texture('sand', T)
    if sand is not None:
        img[beach] = sand[beach]
    else:
        img[beach] = pick(R['sand'], 0.25 + d / 1.6 * 0.5 + (fbm(X, Y, 707, cells=(4, 2)) - 0.5) * 0.4, X, Y)[beach]
    return img


def coast_column(v):
    """The 16x16 original column at x = v * 16 of the coast area: four quarters, each for value v."""
    img = blank()
    for q, dirs in QUARTER_DIRS.items():
        land = [dirs[b] for b in range(3) if v & (1 << b)]
        full = ocean_field(land)
        ox, oy = QUARTER_ORIGIN[q]
        img[oy:oy + 16, ox:ox + 16] = full[oy:oy + 16, ox:ox + 16]
    return img


# Whole-tile coast pieces the game uses for some neighbour patterns instead of quarters.
SPECIAL_COASTS = [(224, 0x1c), (240, 0xc1), (256, 0x07), (272, 0x70), (288, 0x8f), (304, 0xf8)]


def special_coast(mask):
    land = [k for k in range(1, 9) if mask & (1 << (k - 1))]
    img = ocean_field(land)
    # these patterns are diagonal coastlines: fill the land side of the diagonal with beach and grass
    d = land_distance(land)
    lx = np.mean([DIRS[k][0] for k in land])
    ly = np.mean([DIRS[k][1] for k in land])
    side = (X - 15.5) * lx + (Y - 15.5) * ly
    wob = (fbm(X, Y, 711, cells=(8, 4)) - 0.5) * 4
    land_px = side + wob > 9
    shore = (side + wob > 6) & ~land_px
    img[shore] = pick(R['sand'], 0.4 + (fbm(X, Y, 712, cells=(4, 2)) - 0.5) * 0.5, X, Y)[shore]
    img[land_px] = grass_background()[land_px]
    return img


def delta(direction):
    """River mouth entering an ocean tile from direction 1 (N), 3 (E), 5 (S) or 7 (W)."""
    img = blank()
    dx, dy = DIRS[direction]
    # coordinates along (a) and across (c) the river, a = 0 at the tile edge
    if dx == 0:
        a = Y + 0.5 if dy < 0 else T - 0.5 - Y
        c = X - 15.5
    else:
        a = X + 0.5 if dx < 0 else T - 0.5 - X
        c = Y - 15.5
    spread = 2.2 + a * 0.45
    fan = (np.abs(c) < spread) & (a < 13)
    v = 0.75 - a / 13 * 0.5 + (fbm(X, Y, 721, cells=(4, 2)) - 0.5) * 0.3
    img[fan] = pick(R['river'], v, X, Y)[fan]
    bars = fan & (np.abs(np.abs(c) - spread * 0.55) < 0.8) & (a > 3) & (fbm(X, Y, 722, cells=(4, 2)) > 0.45)
    img[bars] = pick(R['sand'], 0.6, X, Y)[bars]
    return img


# ---------- rivers ----------

def _bezier_dist(px, py, p0, p1, p2, steps=40):
    t = np.linspace(0, 1, steps)
    bx = (1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t ** 2 * p2[0]
    by = (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t ** 2 * p2[1]
    d = np.full(px.shape, 99.0)
    for i in range(steps):
        d = np.minimum(d, np.hypot(px - bx[i], py - by[i]))
    return d


RIVER_EDGES = {0: ((15.5, -1.0), (15.5, 6.0)), 1: ((32.0, 15.5), (25.0, 15.5)),
               2: ((15.5, 32.0), (15.5, 25.0)), 3: ((-1.0, 15.5), (6.0, 15.5))}


def river(mask, variant=0):
    """River overlay for connections in mask (bit 0 N, 1 E, 2 S, 3 W); transparent elsewhere."""
    img = blank()
    centre = (15.5 + (variant - 0.5) * 2, 15.5 + (variant - 0.5) * 1.5)
    d = np.full(X.shape, 99.0)
    sides = [b for b in range(4) if mask & (1 << b)]
    for b in sides:
        edge, ctrl = RIVER_EDGES[b]
        # bend each arm a little, always the same way for the same side, so arms meet the edge straight
        bend = ((b * 37 + variant * 11) % 7 - 3) * 1.2
        c = (ctrl[0] + (bend if b in (0, 2) else 0), ctrl[1] + (bend if b in (1, 3) else 0))
        d = np.minimum(d, _bezier_dist(X, Y, edge, c, centre))
    if len(sides) == 1:
        d = np.minimum(d, np.hypot(X - centre[0], Y - centre[1]) - 1.5)
    water = d < 2.4
    bank = (d < 3.6) & ~water
    v = 0.55 + (fbm(X, Y, 731 + variant, cells=(4, 2)) - 0.5) * 0.4
    dn = np.roll(np.roll(d, 1, 0), 1, 1)   # value at x-1, y-1
    up = np.roll(np.roll(d, -1, 0), -1, 1)
    v = np.where(dn >= 2.4, 0.08, np.where(up >= 2.4, 0.95, v))
    img[water] = pick(R['river'], v, X, Y)[water]
    img[bank] = pick(R['mud'], 0.5 + (hash2(X, Y, 735) - 0.5) * 0.8, X, Y)[bank]
    return img
