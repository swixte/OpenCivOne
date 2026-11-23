"""The town ladder on grass: each step open and walled, at game size and 3x, to judge them against each other.
Usage: python ladder.py ENGINE_PNG OUT_JPG"""
import sys
from PIL import Image, ImageDraw
out = sys.argv[2]
sys.argv = [sys.argv[0], sys.argv[1], 'ladder-scene-']
import compose
from compose import CELL, font

ground = Image.open(sys.argv[1]).convert('RGBA').crop((7 * CELL, 6 * CELL, 8 * CELL, 7 * CELL))
row = Image.new('RGBA', (CELL * 6, CELL * 2))
for step in range(6):
    for w in (0, 1):
        row.paste(ground, (step * CELL, w * CELL))
        t = compose.TOWN_ART[(step, bool(w))]
        f = CELL * compose.STEP_SIZE[step] / max(t.size)
        t = t.resize((round(t.width * f), round(t.height * f)), Image.LANCZOS)
        row.alpha_composite(t, (round(step * CELL + (CELL - t.width) / 2), round(w * CELL + (CELL - t.height) / 2)))
big = row.resize((row.width * 3, row.height * 3), Image.LANCZOS)
sheet = Image.new('RGB', (big.width + row.width + 30, big.height + 40), (28, 26, 24))
sheet.paste(row.convert('RGB'), (10, 40))
sheet.paste(big.convert('RGB'), (row.width + 20, 40))
ImageDraw.Draw(sheet).text((10, 10), 'steps 0-5, open (top) and walled; game size and 3x', font=font(18), fill=(240, 232, 214))
sheet.save(out, quality=92)
