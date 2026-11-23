"""Readability tests for the units at game size, shown at 2x so they can be judged on a phone: each unit on grass,
desert, forest, tundra and snow (ships on the sea), then in greyscale (do they still read by value alone?), blurred as if
squinting, and as bare black silhouettes without their bases. Usage: python readtest.py ENGINE_PNG OUT_JPG"""
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
out = sys.argv[2]
sys.argv = [sys.argv[0], sys.argv[1], 'readtest-scene-']
import compose
from compose import CELL, OBJ, font

NAMES = ['settlers', 'militia', 'phalanx', 'legion', 'knights', 'chariot', 'catapult', 'trireme', 'sail']
SEA = (2, 1)
GROUNDS = [('grass', (7, 6)), ('desert', (4, 8)), ('forest', (10, 8)), ('tundra', (5, 3)), ('snow', (6, 1))]
engine = Image.open(sys.argv[1]).convert('RGBA')
compose.TOWNS, compose.ENGINE = [], None


def cell(x, y):
    return engine.crop((x * CELL, y * CELL, (x + 1) * CELL, (y + 1) * CELL))


def row(ground, player=None):
    im = Image.new('RGBA', (CELL * len(NAMES), CELL))
    for i, n in enumerate(NAMES):
        im.paste(cell(*SEA) if n in compose.SHIPS else cell(*ground), (i * CELL, 0))
    compose.UNITS = [(i, 0, n, player or ('red' if i % 2 else 'blue'), False) for i, n in enumerate(NAMES)]
    return compose.draw_units(im, 1).convert('RGB')


def silhouettes():
    im = Image.new('RGB', (CELL * len(NAMES), CELL), (245, 240, 228))
    for i, n in enumerate(NAMES):
        u = compose.body(OBJ + f'unit_{n}.png', CELL, n)[0]
        black = Image.new('RGB', u.size, (20, 18, 16))
        im.paste(black, (round(i * CELL + (CELL - u.width) / 2), round((CELL - u.height) / 2)), u.getchannel('A'))
    return im


rows = [(f'on {name}', row(g)) for name, g in GROUNDS]
rows += [('all blue', row(GROUNDS[0][1], 'blue')), ('all red', row(GROUNDS[0][1], 'red'))]
grass = rows[0][1]
rows += [('greyscale', grass.convert('L').convert('RGB')),
         ('squint (blurred)', grass.filter(ImageFilter.GaussianBlur(1.6))),
         ('silhouette', silhouettes())]
Z, LW = 2, 190
sheet = Image.new('RGB', (LW + CELL * len(NAMES) * Z, len(rows) * (CELL * Z + 8) + 8), (28, 26, 24))
d = ImageDraw.Draw(sheet)
for r, (label, im) in enumerate(rows):
    y = 8 + r * (CELL * Z + 8)
    d.text((12, y + CELL * Z / 2), label, font=font(20), fill=(240, 232, 214), anchor='lm')
    sheet.paste(im.resize((im.width * Z, im.height * Z), Image.NEAREST), (LW, y))
sheet.save(out, quality=92)
