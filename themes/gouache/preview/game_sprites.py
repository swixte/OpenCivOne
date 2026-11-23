"""Exports the painted units and towns as the game's sprites. Units: hd/SP257/X_161_t3.png, the 4x regions of the unit row of SP257
(x = 16 * type + 1, 15x15, so 60x60 at 4x), each 3 original rows taller so tall units reach into the cell above. Each
unit stands on its base exactly as compose.py places it, and the base and the blue player colour on the unit are
written as the primary key colour (magenta shades, 128 = the player's colour), so the game shows them in each player's
colour. Units without painted art keep the 4x set's sprites (assets/hd4).
The town: hd/SP257/193_113_t4_k0-5.png, the city sprite (drawn at the cell's 1, 1, over the painted ground), a town of the
middle of the ladder with a badge in its player's colour (the secondary key, which stands for index 5, the colour the
game swaps to the player's) under the size the game writes at the top left; and 209_113.png, the walls the game draws
over a city that has them, the same town walled. Usage: python game_sprites.py"""
import sys
import numpy as np
from PIL import Image
sys.argv = [sys.argv[0], 'engine-v8.png', 'game-sprites-scene-']
import compose
from compose import OBJ, BASE_SQUASH

# UnitTypeEnum order
TYPES = {'settlers': 0, 'militia': 1, 'phalanx': 2, 'legion': 3, 'knights': 7, 'catapult': 8, 'chariot': 10,
         'trireme': 16, 'sail': 17}
C, SCALE, TALL = 64, 4, 3
OUT = '../hd/SP257/'


def blue_parts(u):
    """How much each pixel of a unit is its blue player colour, 0 to 1 (as compose.team_colour finds it)."""
    h, s, v = [np.asarray(ch, np.float32) for ch in u.convert('RGB').convert('HSV').split()]
    return np.clip((s - 50) / 40, 0, 1) * np.clip(1 - (np.abs(h - 148) - 16) / 10, 0, 1), v


def region(unit):
    # the cell at 4x, with TALL rows of the cell above: the sprite is the cell's 15x15 from (1, 1), so the region is
    # the cell from (4, 4) and its overflow the 12 HD rows above that
    top = TALL * SCALE
    cell = (C, C + top)
    u, (ax, ay), half = compose.body(OBJ + f'unit_{unit}.png', C, unit, 'blue')
    r = min(max(compose.BASE_R.get(unit, 0.3), half / C + 0.05), 0.47)
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
    mask = grey | gear
    out[mask, 0], out[mask, 1], out[mask, 2], out[mask, 3] = key[mask], 0, key[mask], 255
    # elsewhere pixels are either solid or see-through, never a key mixed with something else
    img = Image.fromarray(out.clip(0, 255).astype(np.uint8), 'RGBA')
    return img.crop((SCALE, SCALE, SCALE + 60, SCALE + 60 + top))


# the town step the one city sprite shows, and where the game writes the size: original pixels from the cell's corner
TOWN_STEP, TOWN_TALL = 2, 4
BADGE = (4, 1, 11, 10)


def town(walled):
    top = TOWN_TALL * SCALE
    cell = Image.new('RGBA', (C, C + top), (0, 0, 0, 0))
    t = compose.TOWN_ART[(TOWN_STEP, walled)]
    f = C * compose.STEP_SIZE[TOWN_STEP] / max(t.size)
    t = t.resize((round(t.width * f), round(t.height * f)), Image.LANCZOS)
    cell.alpha_composite(t, (round((C - t.width) / 2), round(top + (C - t.height) / 2)))
    a = np.asarray(cell, np.float32).copy()
    x0, y0, x1, y1 = (v * SCALE for v in BADGE)
    if walled:
        # the walls are drawn over the city after its size, so they leave the badge to the town under them
        a[top + y0:top + y1, x0:x1, 3] = 0
    else:
        # a player-colour badge with a dark edge, as in the previews
        a[top + y0:top + y1, x0:x1] = (20, 18, 16, 255)
        a[top + y0 + SCALE // 2:top + y1 - SCALE // 2, x0 + SCALE // 2:x1 - SCALE // 2] = (0, 100, 100, 255)
    return Image.fromarray(a.clip(0, 255).astype(np.uint8), 'RGBA').crop((SCALE, SCALE, SCALE + 60, SCALE + 60 + top))


if __name__ == '__main__':
    import glob, os
    for unit, t in TYPES.items():
        name = f'{16 * t + 1}_161'
        for old in glob.glob(f'{OUT}{name}*.png'):
            os.remove(old)
        region(unit).save(f'{OUT}{name}_t{TALL}.png')
        print(unit, f'{name}_t{TALL}.png')
    for old in glob.glob(f'{OUT}193_113*.png') + glob.glob(f'{OUT}209_113*.png'):
        os.remove(old)
    town(False).save(f'{OUT}193_113_t{TOWN_TALL}_k0-5.png')
    walls = town(True)
    # the walls region has no art above it
    walls.crop((0, TOWN_TALL * SCALE, 60, 60 + TOWN_TALL * SCALE)).save(f'{OUT}209_113.png')
    print('town', f'193_113_t{TOWN_TALL}_k0-5.png, 209_113.png')
