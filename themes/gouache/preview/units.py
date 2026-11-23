"""All units in a row on the same plain ground, each on its base in its own cell, at game size and 4x with the cell
outline, to judge their sizes and centring against each other. Usage: python units.py ENGINE_PNG OUT_JPG"""
import sys
from PIL import Image, ImageDraw
out = sys.argv[2]
sys.argv = [sys.argv[0], sys.argv[1], 'units-scene-']
import compose
from compose import CELL, BLUE, RED

NAMES = ['settlers', 'militia', 'phalanx', 'legion', 'knights', 'chariot', 'catapult', 'trireme', 'sail']
ground = Image.open(sys.argv[1]).convert('RGBA').crop((7 * CELL, 6 * CELL, 8 * CELL, 7 * CELL))
row = Image.new('RGBA', (CELL * len(NAMES), CELL))
for i, n in enumerate(NAMES):
    row.paste(ground, (i * CELL, 0))
compose.UNITS = [(i, 0, n, 'red' if i % 2 else 'blue', False) for i, n in enumerate(NAMES)]
compose.TOWNS = []
compose.ENGINE = None
im = compose.draw_units(row, 1).convert('RGB')
big = im.resize((im.width * 4, im.height * 4), Image.LANCZOS)
d = ImageDraw.Draw(big)
for i in range(len(NAMES)):
    d.rectangle([i * CELL * 4, 0, (i + 1) * CELL * 4 - 1, CELL * 4 - 1], outline=(255, 255, 255))
sheet = Image.new('RGB', (big.width, big.height + CELL + 20), (28, 26, 24))
sheet.paste(im, (0, 0))
sheet.paste(big, (0, CELL + 20))
sheet.save(out, quality=92)
