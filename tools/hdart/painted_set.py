"""Writes the 4x sprite set that goes with the painted map: units, resources, cities, walls, fortress and huts from the
painted objects (assets/painted/objects, see painted_assets.py), and the remaining map sprites (roads, railroads,
irrigation, mines, pollution, fog, explosions, shield bonus) from the drawn 2x set at twice the size.

Terrain tiles are left out: the painted map replaces them. The output has scale.txt with 4.

Usage: python3 painted_set.py [OUTPUT_DIR] [OBJECTS_DIR]     (defaults: assets/hd4 and assets/painted/objects)

With `print 1` in the style.txt next to the objects folder (the painted art folder), the set is drawn for a woodblock
print map: no cast shadows, every unit and object inside a dark key line and a cream outline so it reads on any ground,
units a little smaller, the flag and the shield marker drawn with key lines, and the city on a ring of light cleared earth.
With icon_*, citizen_*, report_* and news_crowd objects, the city screen icons and citizens, the report portraits and\nthe news audience are drawn from them too.
"""

import os
import sys
import numpy as np
from PIL import Image

import common as c
import overlays as o
import style
import units as u

HERE = os.path.dirname(os.path.abspath(__file__))
S = 4                       # HD pixels per original pixel
UNIT = 15 * S               # units, resources and the city are 15x15 original pixels
CITY_UP = 16                # HD rows of the town that rise into the cell above (a multiple of S)
RESOURCES = ['oasis', 'horses', 'wheat', 'pheasant', 'coal', 'gold', 'caribou', 'seal', 'peat', 'gems', 'fish', 'fish']
SHIPS = range(16, 25)
AIRCRAFT = (14, 15, 25)

OBJECTS = os.path.join(HERE, '..', '..', 'assets', 'painted', 'objects')
PRINT = False
KEY_LINE = (30, 26, 22, 255)
CREAM = (244, 236, 216, 255)


def print_style(objects):
    path = os.path.join(objects, '..', 'style.txt')
    if not os.path.exists(path):
        return False
    return any(line.split()[:2] == ['print', '1'] for line in open(path) if line.strip())


def outlined(img, key=3, cream=3):
    """The image inside a dark key line (key pixels) and a cream outline outside it (cream pixels)."""
    from scipy import ndimage
    a = img[..., 3] > 96
    inner = ndimage.binary_dilation(a, iterations=key) & ~a
    outer = ndimage.binary_dilation(a, iterations=key + cream) & ~a & ~inner
    out = img.copy()
    out[outer] = CREAM
    out[inner] = KEY_LINE
    keep = img[..., 3] > 0
    # the art over its own soft edge, so the key line meets it cleanly
    out[keep & a] = img[keep & a]
    return out
UI = os.path.join(HERE, '..', '..', 'assets', 'painted', 'ui')


def obj(name):
    return np.asarray(Image.open(os.path.join(OBJECTS, name + '.png')).convert('RGBA'))


def up2(img):
    """Drawn 2x art at 4x."""
    h, w = img.shape[:2]
    return np.asarray(Image.fromarray(img).resize((w * 2, h * 2), Image.LANCZOS))


def icon(name, size, bottom=None, shadow=True, painted=False):
    """A painted object fitted into a size x size sprite, standing on its bottom edge, with a soft shadow. In a print it
    gets the key line and cream outline of a token, or, `painted`, only a thin ink line, so it sits in the ground like
    the trees and hills of the map rather than on top of it."""
    img = np.zeros((UNIT, UNIT, 4), dtype=np.uint8)
    if PRINT:
        # room for the outline inside the sprite
        size = min(size + (6 if painted else 0), UNIT - (6 if painted else 14))
    fitted = style.fit(obj(name), width=size, height=size)
    bottom = UNIT - 2 if bottom is None else bottom
    if PRINT:
        style.paste(img, fitted, UNIT / 2, min(bottom, UNIT - (3 if painted else 7)))
        return outlined(img, 1, 0) if painted else outlined(img)
    if shadow:
        style.shadow(img, UNIT / 2, bottom - 1, fitted.shape[1] * 0.42, alpha=90)
    style.paste(img, fitted, UNIT / 2, bottom)
    return img


def unit(j):
    """Unit j in UnitTypeEnum order, with a small flag in the player's colors (key colors) at the top left."""
    img = np.zeros((UNIT, UNIT, 4), dtype=np.uint8)
    if PRINT:
        return print_unit(j)
    fitted = style.fit(obj('unit_' + u.UNIT_NAMES[j]), width=UNIT - 4, height=UNIT - 6)
    if j in AIRCRAFT:
        # flying: the shadow falls on the ground further away
        style.shadow(img, UNIT / 2 + 6, UNIT - 3, fitted.shape[1] * 0.3, alpha=70)
        style.paste(img, fitted, UNIT / 2, UNIT - 8)
    else:
        if j not in SHIPS:
            style.shadow(img, UNIT / 2, UNIT - 3, fitted.shape[1] * 0.42, alpha=90)
        style.paste(img, fitted, UNIT / 2, UNIT - 2)
    flag = c.Sprite(UNIT, UNIT, u.M)
    flag.line(4.5, 1, 4.5, 21, 2, 'wood', 0)
    flag.rect(6, 1, 18, 9, 'P', 0.3)
    flag.rect(6, 9, 18, 12, 'S', 0)
    f = flag.render()
    shown = f[..., 3] > 0
    img[shown] = f[shown]
    return img


CELL = 16 * S
PY, PX = np.mgrid[0:CELL, 0:CELL] + 0.5


def rgba(hexcolor, alpha=255):
    h = hexcolor.lstrip('#')
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)] + [alpha], dtype=np.float64)


def smooth_noise(seed, cells):
    """Smooth noise in [0, 1] over the cell, `cells` features across."""
    rng = np.random.default_rng(seed)
    small = Image.fromarray(np.rint(rng.random((cells + 1, cells + 1)) * 255).astype(np.uint8))
    return np.asarray(small.resize((CELL, CELL), Image.BICUBIC), dtype=np.float64) / 255


def segment(direction):
    """Distance from each pixel to the line from the cell centre toward a direction, and the position along it."""
    dx, dy = o.DIRS[direction]
    c = CELL / 2
    ex, ey = c + dx * (CELL / 2 + 4), c + dy * (CELL / 2 + 4)
    vx, vy = ex - c, ey - c
    length2 = vx * vx + vy * vy
    t = np.clip(((PX - c) * vx + (PY - c) * vy) / length2, 0, 1)
    d = np.hypot(PX - (c + t * vx), PY - (c + t * vy))
    return d, t * np.sqrt(length2)


def paint(img, mask, color):
    """Paints color (RGBA, alpha 0..255) where mask (0..1) is set, over what is there."""
    a = (mask * color[3] / 255)[..., None]
    out = img.astype(np.float64)
    out[..., :3] = out[..., :3] * (1 - a) + color[:3] * a
    out[..., 3:] = np.maximum(out[..., 3:], a * 255)
    return out


def finish(img):
    return np.rint(np.clip(img, 0, 255)).astype(np.uint8)


def print_road(direction):
    """A thin pale dirt road with fine dark edges, slightly wavering."""
    img = np.zeros((CELL, CELL, 4))
    d, along = segment(direction)
    d = d + np.sin(along / 7 + direction) * 0.7
    img = paint(img, np.clip(4.4 - d, 0, 1), rgba('#4a3a28', 210))
    img = paint(img, np.clip(3.3 - d, 0, 1), rgba('#e6d6ae'))
    return finish(img)


def print_railroad(direction):
    """A thin dark railway: two fine rails on sleepers."""
    img = np.zeros((CELL, CELL, 4))
    d, along = segment(direction)
    sleepers = (np.abs((along % 5) - 2.5) < 0.9) & (d < 4.5)
    img = paint(img, sleepers.astype(np.float64), rgba('#5a3e24', 230))
    rails = np.clip(1.1 - np.abs(d - 2.2), 0, 1)
    img = paint(img, rails, rgba('#26221e'))
    return finish(img)


def print_irrigation():
    """Irrigated fields: a patchwork of parcels with thin dikes and a bright channel, fading out at the cell edge."""
    rng = np.random.default_rng(811)
    seeds = rng.random((6, 2)) * CELL
    dist = np.stack([np.hypot(PX - x, PY - y) for x, y in seeds])
    order = np.sort(dist, 0)
    nearest = np.argmin(dist, 0)
    border = order[1] - order[0]
    colors = [rgba(c) for c in ('#8fa055', '#c8a85a', '#a9b572', '#9a8a52', '#7f9a4a', '#bfae66')]
    img = np.zeros((CELL, CELL, 4))
    edge = np.minimum(np.minimum(PX, CELL - PX), np.minimum(PY, CELL - PY))
    fade = np.clip((edge - 2) / 7, 0, 1)
    for i, c in enumerate(colors):
        # furrows running across each parcel
        angle = rng.random() * np.pi
        furrow = 0.86 + 0.14 * np.sin((PX * np.cos(angle) + PY * np.sin(angle)) / 1.3)
        img = paint(img, (nearest == i) * fade, c * np.array([1, 1, 1, 0.8]))
        img[..., :3] = np.where(((nearest == i) & (fade > 0))[..., None], img[..., :3] * furrow[..., None], img[..., :3])
    img = paint(img, np.clip(1.6 - border, 0, 1) * fade, rgba('#4a3a28', 200))
    # the channel meets itself at the cell edges, so neighbouring fields join up
    channel = np.abs(PY - (CELL / 2 + np.sin(PX / CELL * 2 * np.pi) * 4))
    img = paint(img, np.clip(2.6 - channel, 0, 1) * fade, rgba('#2c5f96'))
    img = paint(img, np.clip(1.5 - channel, 0, 1) * fade, rgba('#5fa0d8'))
    return finish(img)


def print_pollution():
    """Smog: grey-brown blotches with a fine key line."""
    edge = np.minimum(np.minimum(PX, CELL - PX), np.minimum(PY, CELL - PY))
    n = (smooth_noise(821, 4) * 0.7 + smooth_noise(822, 9) * 0.3) * np.clip(edge / 12, 0, 1) ** 0.5
    blot = np.clip((n - 0.5) / 0.04, 0, 1)
    img = np.zeros((CELL, CELL, 4))
    img = paint(img, blot, rgba('#6b6256', 200))
    shade = np.clip((n - 0.62) / 0.05, 0, 1)
    img = paint(img, shade, rgba('#4c443a', 200))
    line = np.clip(1 - np.abs(n - 0.5) / 0.012, 0, 1)
    img = paint(img, line, rgba('#2a2420', 230))
    return finish(img)


def print_flag():
    """A small flag in the player's colors with key lines, on a dark pole, for the top left of a unit."""
    flag = c.Sprite(UNIT, UNIT, u.M)
    flag.line(5.5, 2, 5.5, 22, 2, 'dark', 0)
    flag.rect(7, 2, 19, 9, 'P', 0.3)
    flag.rect(7, 9, 19, 12, 'S', 0)
    return flag.render(outline=KEY_LINE, auto_light=False)


def print_unit(j):
    """A unit for the print map: about four fifths of the cell, in a key line and a cream outline, with its flag."""
    img = np.zeros((UNIT, UNIT, 4), dtype=np.uint8)
    fitted = style.fit(obj('unit_' + u.UNIT_NAMES[j]), width=UNIT - 14, height=UNIT - 16)
    bottom = UNIT - 9 if j in AIRCRAFT else UNIT - 6
    style.paste(img, fitted, UNIT / 2 + 3, bottom)
    img = outlined(img)
    f = print_flag()
    shown = f[..., 3] > 0
    img[shown] = f[shown]
    return img


def small(name, width, height, key=2, cream=1):
    """A city screen icon or citizen: the object fitted into width x height original pixels, in a key line and a cream
    outline so it reads on the indigo panels and where the game draws them overlapping."""
    img = np.zeros((height * S, width * S, 4), dtype=np.uint8)
    pad = key + cream
    fitted = style.fit(obj(name), width=width * S - 2 * pad, height=height * S - 2 * pad)
    style.paste(img, fitted, width * S / 2, height * S - pad)
    return outlined(img, key, cream)


# the mini palace on the status board: 3 styles x 4 levels x 5 slices, 7x15 at (160 + 7i, 16 * style),
# i = 5 * level + slice (left edge, inner left, centre, inner right, right edge), joined side by side by the game
PALACE_STYLES = [
    # walls, wall shade, roof, detail
    ((233, 221, 192), (201, 185, 148), (168, 69, 47), (58, 46, 40)),     # classical: cream stone, terracotta roof
    ((242, 238, 228), (205, 196, 180), (62, 74, 99), (58, 46, 40)),      # eastern: white walls, indigo tiles, timber
    ((191, 165, 110), (160, 136, 86), (79, 91, 102), (58, 46, 40)),      # castle: ochre stone, slate towers
]


def print_palace(style_index, level, section):
    """One 7x15 slice of the mini palace, a woodblock building whose slices join without seams."""
    wall, shade, roof, detail = PALACE_STYLES[style_index]
    w, h = 7 * S, 15 * S
    pad = 8
    img = np.zeros((h, w + 2 * pad, 4), dtype=np.uint8)
    x = np.arange(w + 2 * pad) - pad
    top = h - (12 + 8 * level)                      # top of the walls
    for cx in range(w + 2 * pad):
        xi = x[cx]
        t = top
        if section == 0:
            t = top + max(0, (10 - xi)) // 2         # the left edge steps down to the ground line
        if section == 4:
            t = top + max(0, (xi - (w - 11))) // 2
        img[t:, cx] = (*wall, 255)
        if xi % 14 in (2, 3) and style_index == 0:
            img[t + 2:, cx, :3] = shade              # columns
        # roof or crenellations
        if style_index == 2:
            if (xi // 4) % 2 == 0:
                img[max(0, t - 4):t, cx] = (*wall, 255)
        else:
            rt = t - 7
            img[max(0, rt):t, cx] = (*roof, 255)
            if style_index == 1:
                img[max(0, t - 2):t, cx, :3] = detail   # timber beam under the eaves
    rows = range(top + 5, h - 6, 8)
    for y in rows:                                   # windows, one per storey
        img[y:y + 5, pad + 11:pad + 17] = (*detail, 255)
    if section == 2:
        mid = pad + w // 2
        if style_index == 0:                         # a pediment over the centre
            for k in range(10):
                img[max(0, top - 7 - k):top - 7, mid - (14 - k):mid + (14 - k)] = (*roof, 255)
        elif style_index == 1:                       # a tiered roof
            img[max(0, top - 14):top - 7, mid - 9:mid + 9] = (*wall, 255)
            img[max(0, top - 19):top - 14, mid - 12:mid + 12] = (*roof, 255)
        else:                                        # a tower with a pointed roof
            img[max(0, top - 10):top, mid - 8:mid + 8] = (*wall, 255)
            for k in range(12):
                img[max(0, top - 10 - k - 1):top - 10 - k, mid - (9 - k * 3 // 4):mid + (9 - k * 3 // 4)] = (*roof, 255)
        img[h - 10:h, mid - 3:mid + 3] = (*detail, 255)   # the door
    # key line and cream outline, but not across the joins between slices
    if section != 0:
        img[:, :pad] = img[:, pad:pad + 1]
    if section != 4:
        img[:, pad + w:] = img[:, pad + w - 1:pad + w]
    out = outlined(img, 2, 1)
    return out[:, pad:pad + w]


# city screen icons, 7x7 at (129 + 8(i & 3), 33 + 2(i & 4)); the eighth, at (153, 41), is the shield bonus
ICONS = ['food', 'shield', 'trade', 'gold', 'science', 'unhappy', 'luxury']
# city screen citizens, 8x16 at (8i, 128): happy, content and unhappy (two each), then the three specialists
CITIZENS = ['happy_m', 'happy_f', 'content_m', 'content_f', 'unhappy_m', 'unhappy_f', 'entertainer', 'taxman', 'scientist']


def print_mine():
    """The painted mine, a dug terrace with a headframe, inside a key line and a cream outline, filling a whole cell."""
    img = np.zeros((CELL, CELL, 4), dtype=np.uint8)
    fitted = style.fit(obj('mine'), width=CELL - 16, height=CELL - 16)
    style.paste(img, fitted, CELL / 2, CELL - 8)
    return outlined(img)


def print_shield_bonus():
    """The shield marker for the print map: a small red seal stamped into the paper, like the seals on a woodblock print,
    with no outline and a slightly worn edge, so it marks the ground without sitting on it."""
    s = c.Sprite(14, 14, dict(seal=c.ramp('#7a3a26', '#8e4430', '#a2513a', '#b46048', '#c47258'),
                              paper=c.ramp('#c9a88a', '#d6b89a', '#e0c6aa', '#e8d2b8', '#efdcc6')))
    s.poly([(2, 2), (12, 2), (12, 7), (7, 12), (2, 7)], 'seal', 0)
    s.poly([(5, 4), (9, 4), (9, 7), (7, 9), (5, 7)], 'paper', 0)
    img = s.render(outline=None, auto_light=False).copy()
    # stamped ink: a little see-through, unevenly
    noise = smooth_noise(7, 18)[:img.shape[0], :img.shape[1]]
    img[..., 3] = (img[..., 3] * (0.8 + 0.18 * noise)).astype(np.uint8)
    return img


def print_city():
    """The town for the print map, on a ring of light cleared earth that fades into the ground around it, rising into the
    cell above. The game writes the city size at the bottom left of the cell on a print map (from original pixel 1 or 3,
    9), so a small badge sits there, over the edge of the town as on a printed map."""
    h = UNIT + CITY_UP
    img = np.zeros((h, UNIT, 4), dtype=np.uint8)
    y, x = np.mgrid[0:h, 0:UNIT] + 0.5
    d = np.sqrt(((x - UNIT * 0.5) / (UNIT * 0.52)) ** 2 + ((y - (h - UNIT * 0.42)) / (UNIT * 0.44)) ** 2)
    ring = np.clip((1 - d) / 0.3, 0, 1)
    img[..., 0], img[..., 1], img[..., 2] = 240, 230, 200
    img[..., 3] = np.rint(ring * 230).astype(np.uint8)
    town = np.zeros((h, UNIT, 4), dtype=np.uint8)
    fitted = style.fit(obj('town'), width=UNIT - 4, height=h - 8)
    style.paste(town, fitted, UNIT / 2 + 1, h - 6)
    town = outlined(town, key=2, cream=1)
    shown = town[..., 3] > 0
    img[shown] = town[shown]
    badge = c.Sprite(UNIT, h, {'P': 'P', 'S': 'S'})
    # the sprite is drawn one original pixel in from the cell corner, and the game fills original pixels 2..13 of the
    # cell with the player's colors, so the badge stays inside that square
    badge.rect(5, CITY_UP + 29, 35, CITY_UP + 52, 'S', 0)
    badge.rect(7, CITY_UP + 31, 33, CITY_UP + 50, 'P', 0.5)
    b = badge.render(outline=KEY_LINE)
    shown = b[..., 3] > 0
    img[shown] = b[shown]
    return img


def city():
    """The town, rising CITY_UP rows into the cell above, with a badge in the player's colors behind the size number."""
    h = UNIT + CITY_UP
    img = np.zeros((h, UNIT, 4), dtype=np.uint8)
    fitted = style.fit(obj('town'), width=UNIT, height=h - 2)
    style.shadow(img, UNIT / 2, h - 3, UNIT * 0.45, alpha=90)
    style.paste(img, fitted, UNIT / 2, h - 1)
    # with the painted map the game writes the size at original pixels (2..9, 1..6) of this sprite, in its top left corner
    badge = c.Sprite(UNIT, h, {'P': 'P', 'S': 'S'})
    badge.rect(6, CITY_UP + 1, 39, CITY_UP + 29, 'S', 0)
    badge.rect(8, CITY_UP + 3, 37, CITY_UP + 27, 'P', 0.5)
    b = badge.render(outline=(20, 16, 12, 255))
    shown = b[..., 3] > 0
    img[shown] = b[shown]
    return img


def sprites():
    """Yields (sheet, x, y, suffix, image) for every sprite of the set."""
    for i in range(8):
        yield 'SP257', i * 16, 48, '', print_road(i + 1) if PRINT else up2(o.road(i + 1))
        yield 'SP257', 128 + i * 16, 96, '', print_railroad(i + 1) if PRINT else up2(o.railroad(i + 1))
    yield 'SP257', 64, 32, '', print_irrigation() if PRINT else up2(o.irrigation())
    mine = os.path.exists(os.path.join(OBJECTS, 'mine.png'))
    yield 'SP257', 80, 32, '', print_mine() if PRINT and mine else up2(o.mine())
    yield 'SP257', 96, 32, '', print_pollution() if PRINT else up2(o.pollution())
    for i, name in enumerate(RESOURCES):
        # resources are painted into the ground in a print, not set on it as tokens
        yield 'SP257', i * 16 + 1, 113, '', icon('res_' + name, 34, UNIT - 10, painted=True)
    yield 'SP257', 12 * 16 + 1, 113, f'_t{CITY_UP // S}_k0-5', print_city() if PRINT else city()
    yield 'SP257', 13 * 16 + 1, 113, '', icon('walls', UNIT, UNIT - 1, shadow=False)
    yield 'SP257', 14 * 16 + 1, 113, '', icon('fortress', UNIT - 6)
    yield 'SP257', 15 * 16 + 1, 113, '', icon('hut', 44, painted=True)
    yield 'SP257', 153, 41, '', up2(print_shield_bonus() if PRINT else o.shield_bonus())
    if os.path.exists(os.path.join(OBJECTS, 'icon_food.png')):
        for i, name in enumerate(ICONS):
            yield 'SP257', 129 + 8 * (i & 3), 33 + 2 * (i & 4), '', small('icon_' + name, 7, 7, 1, 1)
    if os.path.exists(os.path.join(OBJECTS, 'citizen_happy_m.png')):
        for i, name in enumerate(CITIZENS):
            yield 'SP257', 8 * i, 128, '', small('citizen_' + name, 8, 16)
    if PRINT:
        for style_index in range(3):
            for level in range(4):
                for section in range(5):
                    yield 'SP257', 160 + 7 * (5 * level + section), 16 * style_index, '', print_palace(style_index, level, section)
    if os.path.exists(os.path.join(OBJECTS, 'news_crowd.png')):
        # the audience along the bottom of the news screen, 20x9
        yield 'SP257', 176, 128, '', small('news_crowd', 20, 9, 1, 1)
    for i, direction in enumerate((1, 3, 5, 7)):
        yield 'SP257', 80 + i * 16, 128, '', up2(o.fog(direction))
    for i in range(8):
        yield 'SP257', i * 16 + 1, 97, '', up2(o.explosion(i))
    for j in range(len(u.UNIT_NAMES)):
        yield 'SP257', (j % 20) * 16 + 1, (j // 20) * 16 + 161, '', unit(j)


def tile(name, width, height, scale, mean, spread, tint=(1.0, 1.0, 1.0)):
    """A width x height tile that repeats without seams, from part of a seamless interface texture shown `scale` times its
    size, with its brightness set to `mean` and its contrast to `spread` (standard deviation)."""
    tex = Image.open(os.path.join(UI, name + '.png')).convert('RGB')
    cw, ch = round(width / scale), round(height / scale)
    crop = tex.crop((0, 0, cw, ch)).resize((width, height), Image.LANCZOS)
    a = np.asarray(crop, dtype=np.float64)
    # blend with a half-shifted copy so the tile repeats without seams in both directions
    b = np.roll(np.roll(a, height // 2, 0), width // 2, 1)
    y, x = np.mgrid[0:height, 0:width] + 0.5
    w = np.minimum(np.minimum(x, width - x) / (width / 2), np.minimum(y, height - y) / (height / 2))
    w = np.clip(w * 1.6 - 0.3, 0, 1)[..., None]
    w = w * w * (3 - 2 * w)
    a = a * w + b * (1 - w)
    lum = a.mean(-1, keepdims=True)
    a = (a - lum) + mean + (lum - lum.mean()) * (spread / max(lum.std(), 1e-6))
    a = a * np.array(tint)
    out = np.concatenate([np.clip(a, 0, 255), np.full((height, width, 1), 255.0)], -1)
    return np.rint(out).astype(np.uint8)


def ui():
    """Yields (sheet, x, y, suffix, image) for the interface patterns, and the grain for flat boxes as image files."""
    if os.path.exists(os.path.join(OBJECTS, 'report_spy.png')):
        # portraits beside report messages, 41x58 at (160 + 42i, 142): spies, diplomats, travelers
        for i, name in enumerate(('spy', 'diplomat', 'traveler')):
            yield 'SP299', 160 + 42 * i, 142, '', small('report_' + name, 41, 58)
    if PRINT:
        # paper: cream behind the sidebar and dialogs, indigo behind the city screen (it has light and colored text)
        yield 'SP299', 288, 120, '', tile('stone', 32 * S, 16 * S, 0.25, 226, 6, (0.98, 1.0, 1.03))
        yield 'SP299', 208, 100, '', tile('slate', 16 * S, 16 * S, 0.25, 84, 6, (1.0, 1.0, 1.0))
        return
    # SP299 32x16 at (288, 120): the stone behind the sidebar, status panel and dialogs; light enough for black text
    yield 'SP299', 288, 120, '', tile('stone', 32 * S, 16 * S, 0.17, 150, 22)
    # SP299 16x16 at (208, 100): the city screen background; dark for its light and colored text
    yield 'SP299', 208, 100, '', tile('slate', 16 * S, 16 * S, 0.25, 52, 8, (0.95, 1.0, 1.1))


def grain():
    """The painted grain for flat boxes: mid grey (128 keeps a box's color), low contrast, repeating without seams."""
    g = tile('grain', 256, 256, 0.5, 124, 9)
    return np.repeat(np.rint(g[..., :3].mean(-1, keepdims=True)).astype(np.uint8), 3, -1)


def main(out):
    count = 0
    for sheet, x, y, suffix, img in list(sprites()) + list(ui()):
        folder = os.path.join(out, sheet)
        os.makedirs(folder, exist_ok=True)
        if not (y >= 160 or suffix):
            # only units and the city are meant to carry key colors
            img = c.avoid_keys(img.copy())
        c.save(img, os.path.join(folder, f'{x}_{y}{suffix}.png'))
        count += 1
    with open(os.path.join(out, 'scale.txt'), 'w') as f:
        f.write('4\n')
    # text and other pixel art without HD versions are enlarged with Scale4x
    with open(os.path.join(out, 'upscale.txt'), 'w') as f:
        f.write('scale2x\n')
    os.makedirs(os.path.join(out, 'ui'), exist_ok=True)
    Image.fromarray(grain(), 'RGB').save(os.path.join(out, 'ui', 'material.png'))
    print(f'{count} sprites written to {out}')


if __name__ == '__main__':
    if len(sys.argv) > 2:
        OBJECTS = sys.argv[2]
    PRINT = print_style(OBJECTS)
    # a theme's own interface textures, next to its objects
    if os.path.isdir(os.path.join(OBJECTS, '..', 'ui')):
        UI = os.path.join(OBJECTS, '..', 'ui')
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, '..', '..', 'assets', 'hd4'))
