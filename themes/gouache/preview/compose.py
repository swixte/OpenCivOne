"""Puts the theme's painted town and unit objects (hd/painted/objects) on the engine's preview of its painted map
(engine.png, from the HD layer tests with PAINTED_ART=../hd/painted), each unit on a player-colour circle and each town
with a size badge and name. The engine preview paints terrain only, so this stands in for the game's own drawing.
Usage: python compose.py [ENGINE_PNG] [OUT_PREFIX] [TOWN_SCALE]"""
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

CELL, OBJ = 64, '../hd/painted/objects/'
BLUE, RED, INK = (48, 88, 190), (192, 52, 40), (20, 18, 16)


def font(size):
    for n in ('georgiab.ttf', 'DejaVuSerif-Bold.ttf'):
        try:
            return ImageFont.truetype(n, size)
        except OSError:
            pass
    return ImageFont.load_default()




def soft_town(path):
    """A town with its outskirts faded out: the keyed edge is pulled in, feathered and cleared of magenta spill."""
    t = Image.open(path).convert('RGBA')
    a = np.asarray(t, np.float32)
    rgb, alpha = a[..., :3], a[..., 3]
    pink = (rgb[..., 0] - rgb[..., 1] > 25) & (rgb[..., 2] - rgb[..., 1] > 25)
    earth = np.median(rgb[(alpha > 250) & ~pink], axis=0)
    rgb[pink] = earth
    soft = Image.fromarray(alpha.astype('uint8')).filter(ImageFilter.MinFilter(7)).filter(ImageFilter.GaussianBlur(6))
    alpha = np.minimum(alpha, np.asarray(soft, np.float32))
    # the town's own ground (gardens, earth) lets the terrain under it show through: only roofs, walls and their dark
    # outlines stay solid, so a town on rock, snow or sand stands on rock, snow or sand
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    bright, sat = rgb.mean(-1), rgb.max(-1) - rgb.min(-1)
    built = ((r > g * 1.3) & (r > 110)) | ((bright > 165) & (sat < 70)) | (bright < 60)
    built = np.asarray(Image.fromarray((built * 255).astype('uint8')).filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.GaussianBlur(2)), np.float32) / 255
    alpha = alpha * (0.3 + 0.7 * built)
    # more contrast than the generated painting has, so a town stands out from the terrain round it
    rgb = (rgb - 110) * 1.25 + 110
    # and brighter, richer roofs and lanes: the dense towns come out dark and brown, and turn to mush at game size
    grey = rgb.mean(-1, keepdims=True)
    rgb = grey + (rgb - grey) * 1.3
    rgb = 255 * (rgb.clip(0, 255) / 255) ** 0.8
    # marble and pale stone go whiter still, so a capital's white heart shows even on sand
    pale = np.clip((rgb.mean(-1) - 170) / 50, 0, 1) * np.clip(1 - (rgb.max(-1) - rgb.min(-1)) / 90, 0, 1)
    rgb = rgb + (255 - rgb) * (0.6 * pale)[..., None]
    return Image.fromarray(np.dstack([rgb, alpha]).clip(0, 255).astype('uint8'), 'RGBA')


# (cell x, cell y, player, name, size, step 0-5, walled); a town always fits inside its one cell, smaller towns covering
# less of it in a smaller clearing (the engine's, from PAINTED_CITIES with the same steps)
TOWNS = [(6, 4, 'blue', 'Babylon', 9, 4, False), (5, 6, 'blue', 'Ur', 3, 1, False), (3, 7, 'blue', 'Thebes', 14, 5, True),
         (18, 5, 'red', 'Kish', 4, 2, False), (10, 5, 'blue', 'Mari', 5, 2, True),
         (16, 4, 'red', 'Nineveh', 6, 3, True), (15, 7, 'red', 'Assur', 1, 0, False)]
STEP_SIZE = (0.42, 0.56, 0.7, 0.84, 0.98, 0.98)
CITIES = ';'.join(f'{x},{y},{step}' for x, y, _, _, _, step, _ in TOWNS)


UNITS = [(7, 6, 'settlers', 'blue', True), (3, 5, 'phalanx', 'blue', False), (9, 2, 'knights', 'blue', False),
         (5, 9, 'catapult', 'blue', False), (12, 5, 'trireme', 'blue', False),
         (14, 4, 'legion', 'red', False), (14, 6, 'chariot', 'red', False), (19, 10, 'sail', 'red', False)]


# how round a unit's base looks from the camera's height: 1 straight down, about 0.3 side-on
BASE_SQUASH = 0.62


def unit_base(col, c, active, ss=4):
    """A unit's round base seen from the same steep angle as the figure (about 65 degrees), like a miniature's on a table:
    a slightly squashed disc in the player's colour with a thin darker side below it, a soft shadow and a highlight on its
    far half. Drawn large and scaled down, so its edges are smooth. Centred on the middle of its top."""
    rx, ry, side = c * 0.4 * ss, c * 0.4 * BASE_SQUASH * ss, c * 0.02 * ss
    w, h = round(2 * rx + 12 * ss), round(2 * ry + 2 * side + 12 * ss)
    cx, cy = w / 2, h / 2
    dark = tuple(int(v * 0.55) for v in col)
    light = tuple(min(255, int(v * 1.3 + 40)) for v in col)
    im = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(im).ellipse([cx - rx, cy - ry + side * 1.5, cx + rx + side, cy + ry + side * 2], fill=(0, 0, 0, 100))
    im = im.filter(ImageFilter.GaussianBlur(side * 0.7))
    d = ImageDraw.Draw(im)
    if active:
        d.ellipse([cx - rx - 2.5 * ss, cy - ry - 2.5 * ss, cx + rx + 2.5 * ss, cy + ry + side + 2.5 * ss], outline='white', width=round(1.5 * ss))
    d.ellipse([cx - rx, cy - ry + side, cx + rx, cy + ry + side], fill=dark)
    d.rectangle([cx - rx, cy, cx + rx, cy + side], fill=dark)
    d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=col)
    hl = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(hl).ellipse([cx - rx * 0.8, cy - ry * 0.85, cx + rx * 0.5, cy - ry * 0.1], fill=light + (120,))
    im.alpha_composite(hl.filter(ImageFilter.GaussianBlur(ry * 0.25)))
    return im.resize((round(w / ss), round(h / ss)), Image.LANCZOS)


def body(path, c, unit):
    """A unit scaled by its body, not its outline, so a spear, lance, flag or oars do not shrink it: the box round its
    solid parts fits the same space for every unit. Returns it with the point that goes on the middle of the base: the
    middle of its body across and its feet (the bottom of its body, a little up) down for those that stand on the ground, and the middle of the
    body for ships, which sit in the base like in water."""
    u = Image.open(path).convert('RGBA')
    solid = np.asarray(u.getchannel('A').filter(ImageFilter.MinFilter(11)), np.float32) > 128
    ys, xs = np.nonzero(solid)
    w, h = np.percentile(xs, 97) - np.percentile(xs, 3), np.percentile(ys, 97) - np.percentile(ys, 3)
    f = min(c * 0.5 / w, c * 0.5 / h, c * 0.95 / max(u.size)) * UNIT_SIZE.get(unit, 1)
    u = u.resize((round(u.width * f), round(u.height * f)), Image.LANCZOS)
    if unit in SHIPS:
        return u, (xs.mean() * f, ys.mean() * f)
    return u, ((np.percentile(xs, 3) + w / 2) * f, (np.percentile(ys, 97) - 0.15 * h) * f)


SHIPS = {'trireme', 'sail', 'frigate', 'ironclad', 'cruiser', 'battleship', 'submarine', 'carrier', 'transport'}
# a few units read bigger or smaller than their box says (a broad shield, a lance across a horse)
UNIT_SIZE = {'settlers': 1.15, 'catapult': 1.15, 'phalanx': 0.92, 'legion': 0.9, 'trireme': 1.1}

def scene(scale):
    base = Image.open(ENGINE).convert('RGBA')
    im = base.resize((base.width * scale, base.height * scale), Image.LANCZOS)
    c = CELL * scale
    d = ImageDraw.Draw(im)
    for x, y, player, name, size, step, walled in TOWNS:
        t = TOWN_ART[(step, walled)]
        f = c * STEP_SIZE[step] / max(t.size)
        t = t.resize((round(t.width * f), round(t.height * f)), Image.LANCZOS)
        tx, ty = round(x * c + (c - t.width) / 2), round(y * c + (c - t.height) / 2)
        im.alpha_composite(t, (tx, ty))
        # the badge sits on the town's top-left corner and the name just under it, however small the town is
        s, col = 13 * scale, BLUE if player == 'blue' else RED
        bx, by = max(x * c + 1 * scale, tx - s + 2 * scale), max(y * c + 1 * scale, ty - 4 * scale)
        d.rectangle([bx, by, bx + s, by + s], fill=col, outline=INK, width=scale)
        d.text((bx + s / 2, by + s / 2), str(size), font=font(10 * scale), fill='white', anchor='mm')
        ny = min((y + 1) * c, ty + t.height + 2 * scale)
        for o in (2, 1):
            d.text((x * c + c / 2 + o * scale, ny + o * scale), name, font=font(12 * scale), fill=INK, anchor='mt')
        d.text((x * c + c / 2, ny), name, font=font(12 * scale), fill=(255, 250, 235), anchor='mt')
    draw_units(im, scale)
    return im.convert('RGB')


def draw_units(im, scale):
    """Each unit on its base, on the image im drawn at scale."""
    c = CELL * scale
    for x, y, unit, player, active in UNITS:
        # a round base seen from the same steep angle as the figure, like a miniature's: a slightly squashed disc with
        # a thin darker side below it, in the player's colour; the figure, seen mostly from above, sits over its middle
        cx, cy = x * c + c / 2, y * c + c * 0.6
        b = unit_base(BLUE if player == 'blue' else RED, c, active)
        im.alpha_composite(b, (round(cx - b.width / 2), round(cy - b.height / 2)))
        u, (bx, by) = body(OBJ + f'unit_{unit}.png', c, unit)
        im.alpha_composite(u, (round(cx - bx), round(cy - by)))
    return im


# the ladder of town steps: a hamlet of huts, three steps of terracotta towns, a city with a gold-domed palace and a
# marble capital; walled versions from step 1 up
TOWN_ART = {(i, w): soft_town(f'../hd/painted/towns/ladder/t{i}_{"w" if w else "o"}.png') for i in range(6) for w in (False, True)}
ENGINE = sys.argv[1] if len(sys.argv) > 1 else 'engine.png'
OUT = sys.argv[2] if len(sys.argv) > 2 else ''
scene(1).save(OUT + 'map.jpg', quality=90)
scene(2).crop((256, 256, 1408, 1024)).save(OUT + 'zoom.jpg', quality=90)
