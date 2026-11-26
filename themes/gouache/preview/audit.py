"""An audit sheet of the current map: every town and unit in its 3x3 neighbourhood at 3x, labelled.
Usage: python audit.py ENGINE_PNG OUT_PREFIX"""
import sys
from PIL import Image, ImageDraw
sys.argv = [sys.argv[0], sys.argv[1], sys.argv[2] + 'scene-']
import compose
from compose import CELL, TOWNS, UNITS, font

STEPS = ('hamlet', 'village', 'town', 'large town', 'city', 'capital')
im = compose.scene(1)
Z, S = 3, 3 * CELL * 3
items = [(x, y, f'{name}  (size {size}, step {step} {STEPS[step]}{", walled" if walled else ""})')
         for x, y, _, name, size, step, walled in TOWNS]
items += [(x, y, f'{unit} ({player}{", active" if active else ""})') for x, y, unit, player, active in UNITS]
cols = 4
rows = (len(items) + cols - 1) // cols
sheet = Image.new('RGB', (cols * (S + 20) + 20, rows * (S + 50) + 10), (28, 26, 24))
d = ImageDraw.Draw(sheet)
for i, (x, y, label) in enumerate(items):
    crop = im.crop(((x - 1) * CELL, (y - 1) * CELL, (x + 2) * CELL, (y + 2) * CELL)).resize((S, S), Image.LANCZOS)
    x0, y0 = 20 + (i % cols) * (S + 20), 10 + (i // cols) * (S + 50)
    d.text((x0, y0 + 8), label, font=font(18), fill=(240, 232, 214))
    sheet.paste(crop, (x0, y0 + 40))
sheet.save(sys.argv[2][:-6] + 'closeups.jpg', quality=90)
