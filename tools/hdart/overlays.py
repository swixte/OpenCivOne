"""Map overlays: roads, railroads, irrigation, mines, pollution, special resources, cities, fog and explosions."""

import numpy as np
from common import T, grid, fbm, hash2, ramp, pick, blank, Sprite, key_primary, poly_mask

X, Y = grid()
DIRS = {1: (0, -1), 2: (1, -1), 3: (1, 0), 4: (1, 1), 5: (0, 1), 6: (-1, 1), 7: (-1, 0), 8: (-1, -1)}

DIRT = ramp('#4a3520', '#6b4e2e', '#8a6a40', '#a88552', '#c4a26c')
RAIL = ramp('#2c2c30', '#4a4a52', '#70707a', '#9a9aa4', '#c8c8d0')
SLEEPER = ramp('#3a2614', '#55391e', '#6f4d2a')
WATER = ramp('#1d4f8f', '#2a68ab', '#3a84c4', '#5aa3d6', '#8cc4e6')
CROP = ramp('#4f6a1c', '#6a8a26', '#88a834', '#a8c448', '#c8dc6a')


def _segment(direction, width):
    """Distance field and position along a segment from the tile centre toward a direction."""
    dx, dy = DIRS[direction]
    ex, ey = 15.5 + dx * 17, 15.5 + dy * 17
    px, py = X - 15.5, Y - 15.5
    lx, ly = ex - 15.5, ey - 15.5
    ll = lx * lx + ly * ly
    t = np.clip((px * lx + py * ly) / ll, 0, 1)
    d = np.hypot(px - t * lx, py - t * ly)
    return d, t


def road(direction):
    img = blank()
    d, t = _segment(direction, 5)
    m = d < 2.8
    v = 0.6 - d / 2.8 * 0.35 + (fbm(X, Y, 801, cells=(4, 2)) - 0.5) * 0.4
    img[m] = pick(DIRT, v, X, Y)[m]
    edge = (d >= 2.8) & (d < 3.6)
    img[edge] = (60, 44, 26, 150)
    ruts = m & (np.abs(d - 1.3) < 0.45) & (hash2(X, Y, 802) < 0.6)
    img[ruts] = DIRT[1]
    return img


def railroad(direction):
    img = blank()
    dx, dy = DIRS[direction]
    d, t = _segment(direction, 6)
    length = 17 * np.hypot(dx, dy)
    along = t * length
    # sleepers across the track every 3 pixels, then two rails
    sleeper = (d < 3.6) & ((np.floor(along) % 3) == 0)
    img[sleeper] = pick(SLEEPER, 0.5, X, Y)[sleeper]
    bed = (d < 4.2) & ~sleeper
    img[bed] = (70, 64, 58, 120)
    rails = np.abs(d - 2.0) < 0.6
    img[rails] = pick(RAIL, 0.75 - (d - 1.4) * 0.5, X, Y)[rails]
    return img


def irrigation():
    img = blank()
    inside = (X >= 2) & (X < 30) & (Y >= 2) & (Y < 30)
    row = (Y.astype(int) - 2) % 6
    ditch = inside & (row == 0)
    crop = inside & (row >= 2) & (row <= 4) & (hash2(X // 2, Y // 6, 811) < 0.85)
    img[crop] = pick(CROP, 0.45 + (row[crop] == 2) * 0.3 - (row[crop] == 4) * 0.25 + (hash2(X, Y, 812)[crop] - 0.5) * 0.3, X[crop], Y[crop])
    img[ditch] = pick(WATER, 0.55 + (fbm(X, Y, 813, cells=(4, 2)) - 0.5) * 0.6, X, Y)[ditch]
    bank = inside & (row == 1)
    img[bank] = (74, 58, 32, 200)
    return img


MATS = dict(
    wood=ramp('#3d2814', '#5a3c1e', '#7a5530', '#9c7244', '#bd925e'),
    dark=ramp('#08080a', '#141418', '#202026', '#2e2e36', '#3e3e48'),
    rock=ramp('#3d3832', '#56504a', '#706860', '#8a8278', '#a49c92'),
    metal=ramp('#3a3e44', '#5a6068', '#7e868e', '#a6aeb6', '#d0d6dc'),
    gold=ramp('#6a4a08', '#9a700e', '#c89a1e', '#e8c040', '#fae27a'),
    coal=ramp('#0c0c0e', '#1a1a1e', '#2a2a30', '#3e3e46', '#5a5a64'),
    palm=ramp('#123a14', '#1d5a1e', '#2e7a28', '#47983a', '#6cb850'),
    sand=ramp('#8a6a3a', '#a5824a', '#c09c5e', '#d6b676', '#e6cc93'),
    water=WATER,
    horse=ramp('#3a2010', '#5a3418', '#7a4a22', '#9a6430', '#ba8044'),
    mane=ramp('#140c08', '#24160e', '#382418'),
    white=ramp('#9aa2aa', '#bcc4cc', '#dce2e8', '#f2f6f8', '#ffffff'),
    feather=ramp('#4a2a10', '#7a4a1a', '#a86a26', '#c8883a', '#e0a858'),
    red=ramp('#5a1010', '#8a1a18', '#b82a22', '#d84838', '#f07060'),
    green=ramp('#1a4a1a', '#2a6a24', '#3c8a30', '#58aa44', '#7cc85e'),
    seal=ramp('#2a2a30', '#44444e', '#62626e', '#82828e', '#a4a4b0'),
    peat=ramp('#2a1a0e', '#3e2814', '#54381c', '#6c4a26', '#866034'),
    gem=ramp('#3a0a5a', '#5a1a8a', '#8a3ac0', '#b46ae0', '#dca8f8'),
    fish=ramp('#2a4a6a', '#3e6a8e', '#5a8eb2', '#80b2d2', '#b0d6ec'),
    stone=ramp('#4a443c', '#6a6258', '#8a8276', '#aaa294', '#cac2b2'),
    roof=ramp('#5a2a18', '#7a3a20', '#9a4c2a', '#ba6438', '#d68250'),
    straw=ramp('#6a5220', '#8a6c2a', '#aa8838', '#c8a64a', '#e2c264'),
    earth=ramp('#3a2c18', '#54401e', '#6e5428', '#886a34', '#a28242'),
    fire=ramp('#7a1404', '#c03008', '#f06810', '#ffa830', '#ffe080'),
    smoke=ramp('#1a1818', '#302c2a', '#48423e', '#625a54', '#7e766e'),
    P='P', S='S',
)


def mine():
    s = Sprite(T, T, MATS)
    s.poly([(4, 28), (12, 12), (20, 10), (28, 28)], 'rock', 0)
    s.poly([(12, 28), (12, 19), (16, 16), (20, 19), (20, 28)], 'dark', -1)
    s.rect(10, 18, 12, 29, 'wood', 0)
    s.rect(20, 18, 22, 29, 'wood', -0.5)
    s.rect(10, 17, 22, 19, 'wood', 0.5)
    s.rect(22, 25, 28, 28, 'metal', 0)
    s.ellipse(23.5, 28, 1.3, 1.3, 'dark', 0)
    s.ellipse(26.5, 28, 1.3, 1.3, 'dark', 0)
    s.ellipse(25, 24, 2.2, 1.4, 'coal', 0.5, shade=1)
    return s.render(shadow=(16, 29, 13, 2.5, 80))


def pollution():
    img = blank()
    blot = fbm(X, Y, 821, cells=(8, 4, 2))
    m = blot > 0.55
    v = (blot - 0.55) * 3
    col = pick(MATS['smoke'], 0.2 + v * 0.6, X, Y)
    img[m] = col[m]
    img[m, 3] = 210
    # a skull in the middle
    s = Sprite(T, T, MATS)
    s.ellipse(16, 14, 6, 5.5, 'white', 0, shade=1)
    s.rect(12, 17, 20, 22, 'white', -0.3)
    s.ellipse(13.5, 14, 1.6, 1.8, 'dark', 0)
    s.ellipse(18.5, 14, 1.6, 1.8, 'dark', 0)
    s.rect(14, 20, 15, 22, 'dark', 0)
    s.rect(17, 20, 18, 22, 'dark', 0)
    skull = s.render()
    a = skull[..., 3] > 0
    img[a] = skull[a]
    return img


# ---------- special resources (15x15 original, 30x30 here; drawn at the tile's top left) ----------

def _icon():
    return Sprite(30, 30, MATS)


def res_oasis():
    s = _icon()
    s.ellipse(15, 23, 10, 4, 'water', 0.3)
    s.line(13, 22, 15, 8, 2.2, 'wood', 0)
    for ax, ay in ((7, 9), (23, 9), (9, 5), (21, 5), (15, 4)):
        s.line(15, 8, ax, ay, 2.6, 'palm', 0.4)
    return s.render(shadow=(16, 26, 11, 2.5, 70))


def res_horses():
    s = _icon()
    s.ellipse(14, 17, 7, 4.5, 'horse', 0, shade=1)
    s.poly([(18, 15), (22, 7), (26, 8), (24, 13), (21, 17)], 'horse', 0.3)
    s.ellipse(25, 9, 2.6, 2, 'horse', 0.4)
    s.poly([(19, 13), (22, 7), (23, 8), (21, 14)], 'mane', 0)
    for lx in (9, 12, 17, 20):
        s.rect(lx, 20, lx + 2, 27, 'horse', -0.8)
    s.line(7, 15, 4, 22, 2, 'mane', 0)
    return s.render(shadow=(15, 27, 10, 2.5, 70))


def res_wheat():
    s = _icon()
    for i, x in enumerate((9, 13, 17, 21)):
        s.line(x, 26, 15 + (x - 15) * 0.4, 12, 1.4, 'straw', -0.5)
        s.ellipse(x - (x - 15) * 0.15, 9 + (i % 2), 2, 4, 'straw', 0.5, shade=1)
    s.rect(11, 18, 20, 20, 'wood', 0)
    return s.render(shadow=(15, 27, 9, 2.2, 70))


def res_pheasant():
    s = _icon()
    s.ellipse(14, 17, 7, 5, 'feather', 0, shade=1)
    s.ellipse(21, 11, 3.2, 3, 'green', 0.3, shade=1)
    s.ellipse(22, 13.5, 2.5, 1.2, 'red', 0)
    s.poly([(24, 11), (27, 12), (24, 12.5)], 'gold', 0)
    s.poly([(8, 15), (1, 9), (3, 8), (10, 13)], 'feather', -0.5)
    s.rect(13, 21, 14, 27, 'gold', -1)
    s.rect(16, 21, 17, 27, 'gold', -1)
    return s.render(shadow=(15, 27, 9, 2.2, 70))


def res_coal():
    s = _icon()
    for cx, cy, r in ((10, 20, 5), (19, 21, 5.5), (14, 13, 5), (22, 14, 3.5)):
        s.ellipse(cx, cy, r, r * 0.85, 'coal', 0.3, shade=2)
    return s.render(shadow=(15, 26, 11, 2.5, 70))


def res_gold():
    s = _icon()
    for cx, cy, r in ((10, 19, 4.5), (18, 20, 5), (14, 12, 4)):
        s.ellipse(cx, cy, r, r * 0.8, 'gold', 0.3, shade=2)
    return s.render(shadow=(15, 25, 10, 2.5, 70))


def res_caribou():
    s = _icon()
    s.ellipse(13, 18, 7, 4.5, 'horse', 0.3, shade=1)
    s.ellipse(21, 13, 3, 2.6, 'horse', 0.5)
    for lx in (8, 11, 15, 18):
        s.rect(lx, 21, lx + 2, 27, 'horse', -0.6)
    s.line(21, 11, 18, 4, 1.2, 'white', -1)
    s.line(22, 11, 26, 4, 1.2, 'white', -1)
    s.line(19, 7, 16, 6, 1.0, 'white', -1)
    s.line(25, 7, 28, 6, 1.0, 'white', -1)
    return s.render(shadow=(15, 27, 10, 2.5, 70))


def res_seal():
    s = _icon()
    s.ellipse(14, 19, 9, 5, 'seal', 0.3, shade=1)
    s.ellipse(22, 13, 3.6, 3.2, 'seal', 0.5, shade=1)
    s.poly([(4, 20), (1, 16), (2, 23)], 'seal', 0)
    s.ellipse(23, 12, 0.8, 0.8, 'dark', 0)
    return s.render(shadow=(14, 25, 11, 2.5, 70))


def res_peat():
    s = _icon()
    for i, (x, y) in enumerate(((6, 18), (14, 18), (10, 11), (18, 11), (22, 18))):
        s.rect(x, y, x + 7, y + 6, 'peat', 0.2 - i * 0.05)
    return s.render(shadow=(16, 26, 12, 2.5, 70))


def res_gems():
    s = _icon()
    s.poly([(15, 4), (24, 12), (15, 26), (6, 12)], 'gem', 0)
    s.poly([(15, 4), (24, 12), (15, 12)], 'gem', 1.2)
    s.poly([(15, 12), (24, 12), (15, 26)], 'gem', -0.6)
    s.poly([(6, 12), (15, 4), (15, 12)], 'gem', 0.6)
    return s.render(shadow=(15, 27, 8, 2, 70))


def res_fish(second=False):
    s = _icon()
    for i, (cx, cy) in enumerate(((11, 11), (19, 19)) if not second else ((15, 15),)):
        s.ellipse(cx, cy, 6, 3, 'fish', 0.3, shade=1)
        s.poly([(cx - 5, cy), (cx - 10, cy - 4), (cx - 10, cy + 4)], 'fish', -0.4)
        s.ellipse(cx + 3.5, cy - 0.5, 0.8, 0.8, 'dark', 0)
    return s.render()


RESOURCES = [res_oasis, res_horses, res_wheat, res_pheasant, res_coal, res_gold, res_caribou, res_seal,
             res_peat, res_gems, res_fish, lambda: res_fish(True)]


# ---------- cities, walls, fortress, hut ----------

def city_streets(up=0):
    """City sprite, drawn at the tile's (1, 1) after the game fills the city square with the player color.

    Primary key pixels stand for color index 0: they stay transparent in the original and show the
    player color filled below. Secondary key pixels stand for index 5, which the game swaps to the
    player's second color. Both must stay inside the filled square (x and y 2..25 here).
    The city size is printed over the middle, so the middle is an open square in player color.
    With `up`, the back roofs and a tower rise into the tile above (no key colors there).
    """
    s = Sprite(30, 30 + up, MATS)
    o = up
    s.rect(0, o, 30, 30 + o, 'stone', -0.5)
    s.rect(2, 2 + o, 26, 26 + o, 'P', 0)
    s.rect(8, 7 + o, 22, 21 + o, 'P', 0.6)
    houses = [(2, 2, 8, 7), (9, 2, 14, 6), (16, 2, 21, 6), (22, 2, 26, 8), (2, 9, 6, 15), (23, 10, 26, 16),
              (2, 17, 7, 22), (22, 18, 26, 22), (3, 23, 9, 26), (10, 22, 16, 26), (17, 22, 21, 26)]
    for i, (x0, y0, x1, y1) in enumerate(houses):
        s.rect(x0, y0 + o, x1, y1 + o, 'white', 0.3 - (i % 3) * 0.3)
        s.rect(x0, y0 + o, x1, y0 + o + max(2, (y1 - y0) // 2), 'S', 0.4 - (i % 2) * 0.6)
    if up:
        # gabled roofs of the back row and a tower, above the tile
        for x0, x1 in ((2, 8), (9, 14), (16, 21)):
            s.poly([(x0 - 0.5, 2.5 + o), ((x0 + x1) / 2, 2.5 + o - (x1 - x0) * 0.55), (x1 + 0.5, 2.5 + o)], 'roof', 0.3)
        s.rect(22, 2 + o - up, 27, 8 + o, 'stone', 0.4)
        s.poly([(21.5, 2 + o - up + 0.5), (24.5, -0.5), (27.5, 2 + o - up + 0.5)], 'roof', 0.6)
        s.rect(23.5, 5 + o - up, 25.5, 8 + o - up, 'dark', 0)
    # a spire on the outer right edge, outside the filled square
    s.rect(26, 3 + o, 30, 27 + o, 'stone', 0)
    s.rect(27, o, 29, 6 + o, 'roof', 0.5)
    return s.render(outline=None)


def city_walls():
    s = Sprite(30, 30, MATS)
    s.rect(0, 0, 28, 28, 'stone', 0.2)
    s.rect(3, 3, 25, 25, '', 0)
    for x in range(0, 28, 4):
        s.rect(x, 0, x + 2, 1, '', 0)
        s.rect(x, 27, x + 2, 28, '', 0)
    for cx, cy in ((2, 2), (26, 2), (2, 26), (26, 26)):
        s.ellipse(cx, cy, 3, 3, 'stone', 0.6, shade=1)
    return s.render()


def fortress():
    s = Sprite(30, 30, MATS)
    s.poly([(15, 1), (29, 15), (15, 29), (1, 15)], 'earth', 0.2)
    s.poly([(15, 6), (24, 15), (15, 24), (6, 15)], '', 0)
    for cx, cy in ((15, 3), (27, 15), (15, 27), (3, 15)):
        s.ellipse(cx, cy, 2.6, 2.6, 'stone', 0.5, shade=1)
    return s.render()


def hut():
    s = Sprite(30, 30, MATS)
    for cx, cy, r in ((10, 18, 6), (21, 20, 5.5), (16, 11, 4.5)):
        s.rect(cx - r + 1, cy, cx + r - 1, cy + r * 0.9, 'wood', 0)
        s.poly([(cx - r - 1, cy + 1), (cx, cy - r * 1.1), (cx + r + 1, cy + 1)], 'straw', 0.3)
        s.rect(cx - 1, cy + r * 0.3, cx + 1, cy + r * 0.9, 'dark', 0)
    return s.render(shadow=(16, 27, 12, 2.5, 70))


def shield_bonus():
    s = Sprite(14, 14, MATS)
    s.poly([(1, 1), (13, 1), (13, 7), (7, 13), (1, 7)], 'red', 0.3)
    s.poly([(4, 3), (10, 3), (10, 7), (7, 10), (4, 7)], 'gold', 0.4)
    return s.render()


def fog(direction):
    """Dark edge toward an unexplored neighbour: 1 = N, 3 = E, 5 = S, 7 = W."""
    img = blank()
    dx, dy = DIRS[direction]
    d = np.where(dx < 0, X + 0.5, np.where(dx > 0, T - 0.5 - X, np.where(dy < 0, Y + 0.5, T - 0.5 - Y)))
    a = np.clip(1 - d / 9 + (fbm(X, Y, 831 + direction, cells=(8, 4)) - 0.5) * 0.5, 0, 1)
    # four alpha steps, so the edge keeps a pixel art look
    img[..., 3] = (np.rint(a * 4) / 4 * 255).astype(np.uint8)
    return img


def explosion(frame):
    """Eight frames: a flash, a growing fireball, then smoke."""
    s = Sprite(30, 30, MATS)
    p = frame / 7
    r = 3 + 11 * min(1, p * 1.6)
    if frame < 6:
        s.ellipse(15, 15, r, r, 'fire', 0.6 - p * 1.4, shade=1.5)
        s.ellipse(15, 15, r * 0.55, r * 0.55, 'fire', 1.6 - p * 2)
        for k in range(6):
            ang = k * np.pi / 3 + frame * 0.4
            s.ellipse(15 + np.cos(ang) * r * 0.85, 15 + np.sin(ang) * r * 0.85, r * 0.3, r * 0.3, 'fire', 0.2 - p)
    if frame >= 3:
        for k in range(5):
            ang = k * 1.3 + 0.5
            rr = r * (0.4 + 0.2 * (k % 2))
            s.ellipse(15 + np.cos(ang) * r * 0.6, 15 + np.sin(ang) * r * 0.6 - frame * 0.6, rr, rr, 'smoke', 0.5 - (frame - 3) * 0.2, shade=1)
    return s.render(outline=None)
