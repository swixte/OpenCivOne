"""Writes the HD map art as per-sprite region files, in the layout the game cuts its sprites from.

Usage: python3 generate.py [OUTPUT_DIR] [--style PACK_DIR]     (default output: assets/hd in the repository)

With --style, art from a style pack folder replaces the drawn art where the pack has it (see style.py).

Copy the output folder's contents into the 'hd' folder next to the game's PIC files.
Region files are named X_Y.png, X and Y being the sprite's top left corner in the original
320x200 sheet (see docs/hd-art.md for the sheet layout and how the game uses each sprite).
"""

import os
import sys
import common as c
import terrain as t
import overlays as o
import units as u


CITY_UP = 8     # HD rows of the city that rise into the tile above


def sprites():
    """Yields (sheet, x, y, suffix, image) for every sprite."""
    # TER257.PIC: terrain rows with 16 neighbour variants (rows 6-9 use only the first one)
    # (tall terrains carry extra rows of art above the tile: the _tN suffix, N in original pixels)
    for row, name in enumerate(t.TERRAIN_ROWS):
        tall = f'_t{t.TALL[name] // 2}' if name in t.TALL else ''
        for mask in range(16 if row < 6 else 1):
            yield 'TER257', mask * 16, row * 16, tall, t.terrain_tile(name, mask)
    # TER257.PIC: ocean quarters, one 16x16 column per neighbour value, and river mouths
    for v in range(8):
        yield 'TER257', v * 16, 176, '', t.coast_column(v)
    for i, direction in enumerate((1, 3, 5, 7)):
        yield 'TER257', 128 + i * 16, 176, '', t.delta(direction)

    # SP299.PIC: whole-tile coast pieces and the grass background under every land tile
    for x, mask in t.SPECIAL_COASTS:
        yield 'SP299', x, 100, '', t.special_coast(mask)
    yield 'SP299', 256, 120, '', t.grass_background()

    # SP257.PIC
    for i in range(8):
        yield 'SP257', i * 16, 48, '', o.road(i + 1)
        yield 'SP257', 128 + i * 16, 96, '', o.railroad(i + 1)
    for mask in range(1, 16):
        yield 'SP257', mask * 16, 64, '', t.river(mask, 0)
        yield 'SP257', mask * 16, 80, '', t.river(mask, 1)
    yield 'SP257', 64, 32, '', o.irrigation()
    yield 'SP257', 80, 32, '', o.mine()
    yield 'SP257', 96, 32, '', o.pollution()
    for i, fn in enumerate(o.RESOURCES):
        yield 'SP257', i * 16 + 1, 113, '', fn()
    yield 'SP257', 12 * 16 + 1, 113, f'_t{CITY_UP // 2}_k0-5', o.city_streets(CITY_UP)
    yield 'SP257', 13 * 16 + 1, 113, '', o.city_walls()
    yield 'SP257', 14 * 16 + 1, 113, '', o.fortress()
    yield 'SP257', 15 * 16 + 1, 113, '', o.hut()
    yield 'SP257', 153, 41, '', o.shield_bonus()
    for i, direction in enumerate((1, 3, 5, 7)):
        yield 'SP257', 80 + i * 16, 128, '', o.fog(direction)
    for i in range(8):
        yield 'SP257', i * 16 + 1, 97, '', o.explosion(i)
    for j in range(len(u.UNITS)):
        yield 'SP257', (j % 20) * 16 + 1, (j // 20) * 16 + 161, '', u.unit(j)


def main(out):
    count = 0
    for sheet, x, y, suffix, img in sprites():
        folder = os.path.join(out, sheet)
        os.makedirs(folder, exist_ok=True)
        if not (sheet == 'SP257' and (y >= 160 or suffix)):
            # only units and the city are meant to carry key colors
            img = c.avoid_keys(img.copy())
        c.save(img, os.path.join(folder, f'{x}_{y}{suffix}.png'))
        count += 1
    print(f'{count} sprites written to {out}')


if __name__ == '__main__':
    import style
    here = os.path.dirname(os.path.abspath(__file__))
    args = sys.argv[1:]
    if '--style' in args:
        i = args.index('--style')
        style.load(args[i + 1])
        del args[i:i + 2]
    main(args[0] if args else os.path.join(here, '..', '..', 'assets', 'hd'))
