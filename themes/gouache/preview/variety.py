"""Compares the current town art of steps 3 and 4 with the varied-building variants (raw-big/var_*.png), each in Babylon's
cell of the engine preview, at game size and 3x. Usage: python variety.py ENGINE_PNG OUT_JPG"""
import sys
from PIL import Image, ImageDraw
sys.path.insert(0, '../../../tools/hdart')
from painted_assets import cut_object
from bigcities import soft, font, CELL, X, Y

ROWS = [('Town, step 3', 0.84, [('current', '../hd/painted/towns/ladder/t3_o.png', False),
                                ('organic streets', '../raw-big/var_t3_organic.png', True),
                                ('planned blocks', '../raw-big/var_t3_planned.png', True)]),
        ('City, step 4', 0.98, [('current', '../hd/painted/towns/ladder/t4_o.png', False),
                                ('organic streets', '../raw-big/var_t4_organic.png', True),
                                ('planned blocks', '../raw-big/var_t4_planned.png', True)])]

base = Image.open(sys.argv[1]).convert('RGBA')
crop = ((X - 1) * CELL, (Y - 1) * CELL, (X + 2) * CELL, (Y + 2) * CELL)
Z, W = 3, 3 * CELL * 3
board = Image.new('RGB', (3 * (W + CELL * 3 + 30) + 10, 2 * (W + 50)), (28, 26, 24))
d = ImageDraw.Draw(board)
for r, (title, size, items) in enumerate(ROWS):
    for i, (label, path, raw) in enumerate(items):
        art = Image.open(path)
        town = soft(cut_object(art, size=512) if raw else art.convert('RGBA'))
        f = CELL * size / max(town.size)
        town = town.resize((round(town.width * f), round(town.height * f)), Image.LANCZOS)
        im = base.copy()
        im.alpha_composite(town, (round(X * CELL + (CELL - town.width) / 2), round(Y * CELL + (CELL - town.height) / 2)))
        im = im.crop(crop).convert('RGB')
        x0, y0 = 10 + i * (W + CELL * 3 + 30), r * (W + 50)
        d.text((x0, y0 + 12), f'{title}: {label}', font=font(20), fill=(240, 232, 214))
        board.paste(im, (x0, y0 + 44))
        board.paste(im.resize((W, W), Image.LANCZOS), (x0 + CELL * 3 + 10, y0 + 44))
board.save(sys.argv[2], quality=90)
