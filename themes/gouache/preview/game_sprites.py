"""Exports the painted units and towns as the game's sprites. Units: hd/SP257/X_161_t3.png, the 4x regions of the unit row of SP257
(x = 16 * type + 1, 15x15, so 60x60 at 4x), each 3 original rows taller so tall units reach into the cell above. Each
unit stands on its base exactly as compose.py places it, and the base and the blue player colour on the unit are
written as key colours (128 = the player's colour): the base as the secondary key, the player's deep colour, and the
gear as the primary key, its main colour. Units without painted art yet stand on the same bases, lifted from the 4x set (assets/hd4).
The town: hd/SP257/193_113_t4_k0-5.png, the city sprite (drawn at the cell's 1, 1, over the painted ground), a town of the
middle of the ladder, for a game without the ladder (the game writes the size on a badge of its own); and the
ladder itself, all six steps open and walled, at X_177 after the units, which the game picks from by the city's size.
Usage: python game_sprites.py"""
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
sys.argv = [sys.argv[0], 'engine-v8.png', 'game-sprites-scene-']
import compose
from compose import OBJ, BASE_SQUASH

# UnitTypeEnum order
TYPES = {'settlers': 0, 'militia': 1, 'phalanx': 2, 'legion': 3, 'knights': 7, 'catapult': 8, 'chariot': 10,
         'trireme': 16, 'sail': 17}
# units without painted art yet, from the 4x set (assets/hd4), by UnitTypeEnum
OLD = {'musketeers': 4, 'riflemen': 5, 'cavalry': 6, 'cannon': 9, 'armor': 11, 'mech_infantry': 12, 'artillery': 13,
       'fighter': 14, 'bomber': 15, 'frigate': 18, 'ironclad': 19, 'cruiser': 20, 'battleship': 21, 'submarine': 22,
       'carrier': 23, 'transport': 24, 'nuclear': 25, 'diplomat': 26, 'caravan': 27}
C, SCALE, TALL = 64, 4, 3
OUT = '../hd/SP257/'


def blue_parts(u):
    """How much each pixel of a unit is its blue player colour, 0 to 1 (as compose.team_colour finds it)."""
    h, s, v = [np.asarray(ch, np.float32) for ch in u.convert('RGB').convert('HSV').split()]
    return np.clip((s - 50) / 40, 0, 1) * np.clip(1 - (np.abs(h - 148) - 16) / 10, 0, 1), v


def old_unit(name):
    """A unit of the 4x set lifted off its flag and shadow, a little brighter and richer like the painted units, and
    enlarged to about the painted sheet's scale, as hd/painted/objects/unit_NAME.png, so it stands on a base like them."""
    j = OLD[name]
    u = np.asarray(Image.open(f'../../../assets/hd4/SP257/{16 * (j % 20) + 1}_{161 + 16 * (j // 20)}.png').convert('RGBA'), np.float32).copy()
    r, g, b, al = u[..., 0], u[..., 1], u[..., 2], u[..., 3]
    key = (al == 255) & (((g == 0) & (r == b) & (r > 0)) | ((r == 0) & (g == b) & (g > 0)))
    # the flag: its key colours, a little round them, and the pole down the left edge above the figure
    flag = np.asarray(Image.fromarray((key * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(5)), np.float32) > 0
    flag[:26, :8] = True
    flag[26:] = False
    al[flag] = 0
    # the drop shadow: soft and dark
    al[(al < 230) & (u[..., :3].mean(-1) < 45)] = 0
    rgb = (u[..., :3] - 100) * 1.2 + 140
    grey = rgb.mean(-1, keepdims=True)
    u[..., :3] = grey + (rgb - grey) * 1.3
    img = Image.fromarray(u.clip(0, 255).astype(np.uint8), 'RGBA')
    return img.resize((img.width * 4, img.height * 4), Image.LANCZOS)


def settle_keys(img, top):
    """Keeps key colours only where the game can colour them: art above the sprite has no original pixel of its own,
    so a key pixel there would show whatever is already on the map. Those become a neutral grey of their shade."""
    a = np.asarray(img).copy()
    r, g, b, al = (a[..., i].astype(int) for i in range(4))
    key = (al == 255) & (((g == 0) & (r == b) & (r > 0)) | ((r == 0) & (g == b) & (g > 0)))
    lost = key.copy()
    lost[top:] = False
    shade = np.maximum(r, g)[lost]
    for i, f in enumerate((0.78, 0.76, 0.72)):
        a[..., i][lost] = np.clip(shade * f, 0, 255)
    return Image.fromarray(a, 'RGBA')


def region(unit):
    # the cell at 4x, with TALL rows of the cell above: the sprite is the cell's 15x15 from (1, 1), so the region is
    # the cell from (4, 4) and its overflow the 12 HD rows above that
    top = TALL * SCALE
    cell = (C, C + top)
    u, (ax, ay), half = compose.body(OBJ + f'unit_{unit}.png', C, unit, 'blue')
    r = min(max(compose.BASE_R.get(unit, 0.3), half / C + 0.05) * 1.1, 0.47)
    cx, cy = C / 2, top + C * (0.6 if unit in compose.SHIPS else 0.7)
    # the base drawn in neutral grey: its grey pixels become key shades, its pale rim and soft shadow stay as they are
    base = compose.unit_base((128, 128, 128), C, False, r=r, squash=compose.UNIT_SQUASH.get(unit, BASE_SQUASH))
    layer = Image.new('RGBA', cell, (0, 0, 0, 0))
    layer.alpha_composite(base, (round(cx - base.width / 2), round(cy - base.height / 2)))
    figure = Image.new('RGBA', cell, (0, 0, 0, 0))
    figure.alpha_composite(u, (round(cx - ax), round(cy - ay)))

    b = np.asarray(layer, np.float32)
    f = np.asarray(figure, np.float32)
    fa = f[..., 3:] / 255
    out = b * (1 - fa) + f * fa
    out[..., 3] = 255 * (1 - (1 - b[..., 3] / 255) * (1 - fa[..., 0]))

    # the base's own grey (not its rim or shadow), where the figure doesn't cover it
    grey = (b[..., 3] == 255) & (np.ptp(b[..., :3], axis=-1) < 3) & (fa[..., 0] < 0.5)
    shade = b[..., 0]
    # the figure's blue, where it is solid
    blue, v = blue_parts(figure)
    gear = (blue > 0.5) & (fa[..., 0] > 0.9)
    ref = np.median(v[gear]) if gear.any() else 1
    key = np.where(grey, shade, np.clip(v / ref * 128, 24, 255))
    # the base in the player's deep colour (the secondary key, index 2), the gear in the main one (the primary key,
    # index 10), so a light player's gear stands out from its base
    out[grey, 0], out[grey, 1], out[grey, 2], out[grey, 3] = 0, key[grey], key[grey], 255
    out[gear, 0], out[gear, 1], out[gear, 2], out[gear, 3] = key[gear], 0, key[gear], 255
    # elsewhere pixels are either solid or see-through, never a key mixed with something else
    img = Image.fromarray(out.clip(0, 255).astype(np.uint8), 'RGBA')
    return settle_keys(img.crop((SCALE, SCALE, SCALE + 60, SCALE + 60 + top)), top)


# the town step the one city sprite shows
TOWN_STEP, TOWN_TALL = 2, 4


def town(walled, step=None):
    top = TOWN_TALL * SCALE
    cell = Image.new('RGBA', (C, C + top), (0, 0, 0, 0))
    step = TOWN_STEP if step is None else step
    t = compose.TOWN_ART[(step, walled)]
    f = C * compose.STEP_SIZE[step] / max(t.size)
    t = t.resize((round(t.width * f), round(t.height * f)), Image.LANCZOS)
    cell.alpha_composite(t, (round((C - t.width) / 2), round(top + (C - t.height) / 2)))
    a = np.asarray(cell, np.float32).copy()
    # the game draws the size on its own badge in the corner, sized to the number; a small town steps out of its way
    if step <= 2:
        a = np.roll(a, (SCALE, SCALE), axis=(0, 1))
    return settle_keys(Image.fromarray(a.clip(0, 255).astype(np.uint8), 'RGBA').crop((SCALE, SCALE, SCALE + 60, SCALE + 60 + top)), top)


def irrigation(seed=3):
    """Irrigated fields for 64_32 (drawn over the painted ground of a cell): a patchwork of four plots of furrows,
    turned across each other, with water ditches between them, brushy and fading out at the cell's edge so
    neighbouring irrigated cells run together."""
    rng = np.random.default_rng(seed)
    n = 16 * SCALE
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    # brush grain at full resolution, and a coarse wobble for the patch's edge
    noise = 0.5 + (rng.random((n, n)).astype(np.float32) - 0.5) * 0.5
    wobble = np.asarray(Image.fromarray((rng.random((8, 8)) * 255).astype(np.uint8)).resize((n, n), Image.BICUBIC), np.float32) / 255
    split = n / 2 + (wobble[0, 0] - 0.5) * 8
    plot = (x >= split).astype(int) + 2 * (y >= split).astype(int)
    across = np.where(np.isin(plot, (0, 3)), y, x)
    furrow = (np.sin(across * np.pi / 3.2) > 0).astype(np.float32)
    green = np.array([[104, 150, 52], [140, 170, 66], [118, 146, 58], [150, 160, 70]], np.float32)[plot]
    soil = np.array([96, 82, 46], np.float32)
    rgb = green * (1 - 0.45 * furrow[..., None]) + soil * 0.45 * furrow[..., None]
    rgb *= (0.85 + 0.3 * noise)[..., None]
    ditch = np.minimum(np.abs(x - split), np.abs(y - split))
    water = np.clip(2.2 - ditch, 0, 1)
    bank = np.clip(3.6 - ditch, 0, 1) - water
    rgb = rgb * (1 - water[..., None]) + np.array([70, 130, 190], np.float32) * water[..., None]
    rgb = rgb * (1 - 0.5 * bank[..., None]) + np.array([200, 190, 140], np.float32) * 0.5 * bank[..., None]
    # the patch's edge wanders, so it sits in the ground rather than on it like a square decal
    edge = np.minimum(np.minimum(x, n - 1 - x), np.minimum(y, n - 1 - y)) - wobble * 9
    alpha = np.clip((edge - 1) / 5, 0, 1) * 0.9
    return Image.fromarray(np.dstack([rgb, alpha * 255]).clip(0, 255).astype(np.uint8), 'RGBA')


def shield_bonus():
    """The grassland production bonus for 153_41 (7x7): a small heraldic shield, deep red with a gold rim and a soft
    shadow, smaller and quieter than the 4x set's so the grass shows round it."""
    ss, n = 8, 7 * SCALE
    small = 0.72
    big = n * ss
    im = Image.new('RGBA', (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    def shield(dx, dy, s):
        cx, top, w, h = big / 2 + dx, big * 0.3 + dy, big * 0.56 * s * small, big * 0.66 * s * small
        return [(cx - w / 2, top), (cx + w / 2, top), (cx + w / 2, top + h * 0.45), (cx, top + h), (cx - w / 2, top + h * 0.45)]
    d.polygon(shield(big * 0.06, big * 0.07, 1), fill=(0, 0, 0, 110))
    im = im.filter(ImageFilter.GaussianBlur(big * 0.04))
    d = ImageDraw.Draw(im)
    d.polygon(shield(0, 0, 1), fill=(60, 34, 14, 255))
    d.polygon(shield(0, big * 0.009, 0.86), fill=(186, 152, 84, 255))
    d.polygon(shield(0, big * 0.022, 0.66), fill=(132, 52, 40, 255))
    d.polygon(shield(-big * 0.035, big * 0.015, 0.3), fill=(160, 82, 66, 255))
    return im.resize((n, n), Image.LANCZOS)


if __name__ == '__main__':
    import glob, os
    for unit in OLD:
        old_unit(unit).save(f'{OBJ}unit_{unit}.png')
    for unit, t in {**TYPES, **OLD}.items():
        name = f'{16 * (t % 20) + 1}_{161 + 16 * (t // 20)}'
        for old in glob.glob(f'{OUT}{name}*.png'):
            os.remove(old)
        region(unit).save(f'{OUT}{name}_t{TALL}.png')
        print(unit, f'{name}_t{TALL}.png')
    for old in glob.glob(f'{OUT}193_113*.png'):
        os.remove(old)
    town(False).save(f'{OUT}193_113_t{TOWN_TALL}_k0-5.png')
    # the towns as they grow, in the unused slots after the units in the second unit row (see TownSprites in the game):
    # six steps, then the same six walled; the walls sprite (209_113) stays the 4x set's, as the game also draws it
    # over fortified units
    for i in range(12):
        name = f'{(8 + i) * 16 + 1}_177'
        for old in glob.glob(f'{OUT}{name}*.png'):
            os.remove(old)
        town(i >= 6, i % 6).save(f'{OUT}{name}_t{TOWN_TALL}_k0-5.png')
    print('towns', f'193_113 and the ladder at 129_177 to 305_177')
    irrigation().save(f'{OUT}64_32.png')
    shield_bonus().save(f'{OUT}153_41.png')
    print('irrigation 64_32.png, grassland bonus 153_41.png')
