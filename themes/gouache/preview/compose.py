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
# the step is the one the game draws for the size (MapManagement.TownStep): Thebes is the capital, with the palace
TOWNS = [(6, 4, 'blue', 'Babylon', 12, 4, False), (5, 6, 'blue', 'Ur', 3, 1, False), (3, 7, 'blue', 'Thebes', 14, 5, True),
         (18, 5, 'red', 'Kish', 5, 2, False), (10, 5, 'blue', 'Mari', 5, 2, True),
         (16, 4, 'red', 'Nineveh', 8, 3, True), (15, 7, 'red', 'Assur', 1, 0, False)]
STEP_SIZE = (0.42, 0.56, 0.7, 0.84, 0.98, 0.98)
CITIES = ';'.join(f'{x},{y},{step}' for x, y, _, _, _, step, _ in TOWNS)


UNITS = [(7, 6, 'settlers', 'blue', True), (3, 5, 'phalanx', 'blue', False), (8, 4, 'knights', 'blue', False),
         (5, 9, 'catapult', 'blue', False), (12, 5, 'trireme', 'blue', False),
         (14, 4, 'legion', 'red', False), (14, 6, 'chariot', 'red', False), (19, 10, 'sail', 'red', False)]


# how round a unit's base looks from the camera's height: 1 straight down, about 0.3 side-on
BASE_SQUASH = 0.62


def unit_base(col, c, active, ss=4, r=0.4, squash=BASE_SQUASH):
    """A unit's round base seen from the same steep angle as the figure (about 65 degrees), like a miniature's on a table:
    a slightly squashed disc in the player's colour with a thin darker side below it, a soft shadow and a highlight on its
    far half. Drawn large and scaled down, so its edges are smooth. Centred on the middle of its top."""
    rx, ry, side = c * r * ss, c * r * squash * ss, c * 0.02 * ss
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
    # a thin pale rim round the top, so a blue base still shows on blue sea and a red one on red earth
    d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], outline=(250, 244, 226), width=round(0.9 * ss))
    hl = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(hl).ellipse([cx - rx * 0.8, cy - ry * 0.85, cx + rx * 0.5, cy - ry * 0.1], fill=light + (120,))
    im.alpha_composite(hl.filter(ImageFilter.GaussianBlur(ry * 0.25)))
    return im.resize((round(w / ss), round(h / ss)), Image.LANCZOS)


def team_colour(u, player):
    """The unit with its player colour: the units are painted with blue shields, cloaks, banners and sails (and nothing
    else blue), and for the red player those blues are turned red, keeping their light and shade."""
    if player == 'blue':
        return u
    h, s, v = [np.asarray(ch, np.float32) for ch in u.convert('RGB').convert('HSV').split()]
    blue = np.clip((s - 50) / 40, 0, 1) * np.clip(1 - (np.abs(h - 148) - 16) / 10, 0, 1)
    h = np.where(blue > 0, 2, h)
    s = s + (np.minimum(s * 1.05, 255) - s) * blue
    red = Image.merge('HSV', [Image.fromarray(np.uint8(ch)) for ch in (h, s, v)]).convert('RGB')
    out = Image.composite(red, u.convert('RGB'), Image.fromarray(np.uint8(blue * 255)))
    out.putalpha(u.getchannel('A'))
    return out


def body(path, c, unit, player='blue'):
    """A unit at the one scale all the units were painted at together (units/prompts_sheet.json), so a man on foot is
    smaller than a knight, a chariot or a ship. Returns it with where its base goes: the point on the middle of the
    base and the base's half-width, both in pixels. A unit on land stands on its base where it touches the ground: the
    base is under the lowest part of its solid body (thin spears and poles left out), a man's feet or a cart's wheels,
    centred under and as wide as the whole body. A ship sits in its base as in water, by its middle."""
    u = team_colour(Image.open(path).convert('RGBA'), player)
    solid = np.asarray(u.getchannel('A').filter(ImageFilter.MinFilter(11)), np.float32) > 128
    ys, xs = np.nonzero(solid)
    if unit in SHIPS:
        # a ship sits in the water by its hull, the bottom band of its solid body, not by its middle, which the sails
        # lift: the base is centred on that band and as long as it
        top, bottom = np.percentile(ys, 2), np.percentile(ys, 99)
        hull = ys > bottom - 0.35 * (bottom - top)
        hl, hr = np.percentile(xs[hull], 2), np.percentile(xs[hull], 98)
        ax, ay, half = (hl + hr) / 2, np.median(ys[hull]), (hr - hl) / 2
    else:
        top, bottom = np.percentile(ys, 3), np.percentile(ys, 98)
        # across, the whole body, so a settler and his cart, or a driver and his horses, stand on one base together;
        # a man on foot is centred on his feet alone, the bottom of him, and the base a little further up under him
        fl, fr = np.percentile(xs, 2), np.percentile(xs, 98)
        # the settler walks behind his cart, so his base comes further up to take him in too
        lift = 0.22 if unit == 'settlers' else 0.1
        if unit in FOOT:
            feet = ys > bottom - 0.3 * (bottom - top)
            fl, fr, lift = np.percentile(xs[feet], 2), np.percentile(xs[feet], 98), 0.06
        ax, ay, half = (fl + fr) / 2, bottom - lift * (bottom - top), (fr - fl) / 2
    f = min(c * UNIT_SCALE * UNIT_SIZE.get(unit, 1), c * 0.95 / max(u.size))
    # nothing reaches out of the cell sideways from the middle of the base (the game's sprite leaves the cell's first
    # sixteenth empty): a long pike or a long ship is drawn smaller rather than cut off or pushed off its base
    f = min(f, c * 0.43 / ax, c * 0.49 / (u.width - ax))
    u = u.resize((round(u.width * f), round(u.height * f)), Image.LANCZOS)
    return u, (ax * f, ay * f), half * f


FOOT = {'phalanx', 'legion', 'militia', 'musketeers', 'riflemen', 'diplomat'}
SHIPS = {'trireme', 'sail', 'frigate', 'ironclad', 'cruiser', 'battleship', 'submarine', 'carrier', 'transport'}
# the sheet's pixels per cell: its largest unit, a ship, is about 300 pixels long and a cell across
UNIT_SCALE = 0.92 / 306
# men on foot drawn a little smaller still than the sheet has them, so the size of a unit says what it is
# and their bases as on a tabletop: a small round one for a man on foot, larger for horses and carts, largest for ships
BASE_R = {'phalanx': 0.2, 'legion': 0.2, 'militia': 0.2, 'musketeers': 0.2, 'riflemen': 0.2, 'diplomat': 0.2, 'settlers': 0.38, 'knights': 0.3, 'chariot': 0.32, 'catapult': 0.32,
          'trireme': 0.44, 'sail': 0.42}
UNIT_SIZE = {'phalanx': 0.95, 'legion': 0.95, 'militia': 0.95,
             # the 4x set's units, enlarged to about the sheet's scale, sized against the painted ones
             'musketeers': 0.85, 'riflemen': 0.85, 'diplomat': 0.8, 'cavalry': 1.3, 'cannon': 0.85, 'artillery': 0.9,
             'armor': 0.9, 'mech_infantry': 0.85, 'caravan': 0.85, 'fighter': 0.95, 'bomber': 1.05, 'nuclear': 0.8,
             'frigate': 1.15, 'ironclad': 1.15, 'cruiser': 1.15, 'battleship': 1.2, 'submarine': 1.1, 'carrier': 1.2,
             'transport': 1.15}
# the ships are painted from lower down, nearer side-on, so their bases are flatter ovals to match
UNIT_SQUASH = {'trireme': 0.4, 'sail': 0.48}

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
        # a round base seen from the same angle as the figure, like a miniature's: a squashed disc with a thin darker
        # side below it, in the player's colour, under the figure's feet (or a ship's hull), at least its type's size
        u, (ax, ay), half = body(OBJ + f'unit_{unit}.png', c, unit, player)
        r = min(max(BASE_R.get(unit, 0.3), half / c + 0.05), 0.47)
        cx, cy = x * c + c / 2, y * c + c * (0.6 if unit in SHIPS else 0.7)
        b = unit_base(BLUE if player == 'blue' else RED, c, active, r=r, squash=UNIT_SQUASH.get(unit, BASE_SQUASH))
        im.alpha_composite(b, (round(cx - b.width / 2), round(cy - b.height / 2)))
        im.alpha_composite(u, (round(cx - ax), round(cy - ay)))
    return im


def town_fixes(t, step, walled):
    """Fixes the ladder's art needs at game size: the hamlet's thatch reads as hot orange, so it turns to straw; walls
    of the small walled towns are a hairline, so their ring gets a pale stone top and a dark outer edge; and the large
    walled towns sit on a soft apron of trodden earth, so their square wall doesn't look cut out of the grass."""
    a = np.asarray(t, np.float32).copy()
    rgb, alpha = a[..., :3], a[..., 3]
    if step == 0:
        r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
        orange = np.clip((r - b - 60) / 60, 0, 1) * np.clip((r - g - 15) / 40, 0, 1)
        lum = rgb.mean(-1, keepdims=True)
        straw = np.array([226, 196, 112], np.float32) * (lum / 150)
        rgb[:] = rgb + (straw - rgb) * (0.75 * orange)[..., None]
    size = max(t.size)
    if walled and step in (1, 2):
        solid = Image.fromarray(((alpha > 170) * 255).astype('uint8'))
        w = max(3, round(size * 0.012))
        outer = np.asarray(solid.filter(ImageFilter.MaxFilter(2 * w + 1)), np.float32) / 255
        inner = np.asarray(solid.filter(ImageFilter.MinFilter(2 * w + 1)), np.float32) / 255
        top = np.clip(alpha / 255 - inner, 0, 1)
        rim = np.clip(outer - alpha / 255, 0, 1)
        rgb[:] = rgb + (np.array([236, 222, 186], np.float32) - rgb) * (0.85 * top)[..., None]
        rgb[:] = rgb * (1 - rim[..., None]) + np.array([58, 44, 30], np.float32) * rim[..., None]
        alpha[:] = np.maximum(alpha, rim * 230)
    out = Image.fromarray(np.dstack([rgb, alpha]).clip(0, 255).astype('uint8'), 'RGBA')
    if walled and step >= 4:
        pad = round(size * 0.06)
        apron = Image.new('RGBA', (t.width + 2 * pad, t.height + 2 * pad), (0, 0, 0, 0))
        m = Image.new('L', apron.size, 0)
        m.paste(t.getchannel('A'), (pad, pad))
        m = m.filter(ImageFilter.MaxFilter(2 * (pad // 2) + 1)).filter(ImageFilter.GaussianBlur(pad / 2))
        earth = Image.new('RGBA', apron.size, (150, 120, 78, 255))
        earth.putalpha(m.point(lambda v: v * 0.55))
        apron.alpha_composite(earth)
        apron.alpha_composite(out, (pad, pad))
        out = apron
    return out


# the ladder of town steps: a hamlet of huts, three steps of terracotta towns, a city with a gold-domed palace and a
# marble capital; walled versions from step 1 up
TOWN_ART = {(i, w): town_fixes(soft_town(f'../hd/painted/towns/ladder/t{i}_{"w" if w else "o"}.png'), i, w)
            for i in range(6) for w in (False, True)}
ENGINE = sys.argv[1] if len(sys.argv) > 1 else 'engine.png'
OUT = sys.argv[2] if len(sys.argv) > 2 else ''
scene(1).save(OUT + 'map.jpg', quality=90)
scene(2).crop((256, 256, 1408, 1024)).save(OUT + 'zoom.jpg', quality=90)
