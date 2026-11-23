"""Compares unit camera angles (side-on, 45, 65 and straight down; raw-units/unit_*_a*.png) on the map next to a town,
at game size and 3x. Usage: python angles.py ENGINE_PNG OUT_JPG"""
import sys
from PIL import Image, ImageDraw, ImageFilter
sys.path.insert(0, '../../../tools/hdart')
from painted_assets import cut_object
out = sys.argv[2]
sys.argv = [sys.argv[0], sys.argv[1], 'angles-scene-']
import compose
from compose import CELL, BLUE, font

ROWS = [('phalanx', 7, 4), ('knights', 7, 4), ('catapult', 7, 4), ('trireme', 11, 4)]
COLS = [('side-on (now)', None, 0.3, 0.035), ('45 degrees', 'a45', 0.42, 0.03), ('65 degrees', 'a65', 0.62, 0.02),
        ('straight down', 'a90', 1.0, 0.0)]


def base(col, c, squash, side, ss=4):
    """compose.unit_base with its own squash: rounder and thinner the higher the camera."""
    rx, ry, side = c * 0.4 * ss, c * 0.4 * squash * ss, c * side * ss
    w, h = round(2 * rx + 12 * ss), round(2 * ry + 2 * side + 12 * ss)
    cx, cy = w / 2, h / 2
    dark = tuple(int(v * 0.55) for v in col)
    light = tuple(min(255, int(v * 1.3 + 40)) for v in col)
    im = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(im).ellipse([cx - rx + 2 * ss, cy - ry + 2 * ss + side, cx + rx + 2 * ss, cy + ry + 2 * ss + side], fill=(0, 0, 0, 100))
    im = im.filter(ImageFilter.GaussianBlur(2 * ss))
    d = ImageDraw.Draw(im)
    if side:
        d.ellipse([cx - rx, cy - ry + side, cx + rx, cy + ry + side], fill=dark)
        d.rectangle([cx - rx, cy, cx + rx, cy + side], fill=dark)
    else:
        d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=dark)
        rx, ry = rx - 2 * ss, ry - 2 * ss
    d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=col)
    hl = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(hl).ellipse([cx - rx * 0.8, cy - ry * 0.85, cx + rx * 0.5, cy - ry * 0.1], fill=light + (110,))
    im.alpha_composite(hl.filter(ImageFilter.GaussianBlur(ry * 0.25)))
    return im.resize((round(w / ss), round(h / ss)), Image.LANCZOS)


compose.UNITS = []
scene = compose.scene(1).convert('RGBA')
c, Z = CELL, 3
tile = 3 * c
board = Image.new('RGB', (len(COLS) * (tile * (1 + Z) + 30) + 10, len(ROWS) * (tile * Z + 20) + 50), (28, 26, 24))
d = ImageDraw.Draw(board)
for j, (label, *_rest) in enumerate(COLS):
    d.text((10 + j * (tile * (1 + Z) + 30), 12), label + '   (game size, then 3x)', font=font(20), fill=(240, 232, 214))
for i, (unit, x, y) in enumerate(ROWS):
    for j, (label, angle, squash, side) in enumerate(COLS):
        im = scene.copy()
        if angle:
            u = cut_object(Image.open(f'../raw-units/unit_{unit}_{angle}.png'), size=512)
        else:
            u = Image.open(f'../hd/painted/objects/unit_{unit}.png').convert('RGBA')
        b = base(BLUE, c, squash, side)
        top = squash >= 0.6
        cx, cy = x * c + c / 2, y * c + (c * 0.62 if top else c * 0.78)
        im.alpha_composite(b, (round(cx - b.width / 2), round(cy - b.height / 2)))
        f = c * (0.8 if top else 0.88) / max(u.size)
        u = u.resize((round(u.width * f), round(u.height * f)), Image.LANCZOS)
        # side-on figures stand on the base; seen from above they sit over its middle
        fy = cy - u.height / 2 if top else cy + c * 0.4 * squash * 0.3 - u.height
        im.alpha_composite(u, (round(cx - u.width / 2), round(fy)))
        crop = im.crop(((x - 1) * c, (y - 1) * c, (x + 2) * c, (y + 2) * c)).convert('RGB')
        x0, y0 = 10 + j * (tile * (1 + Z) + 30), 50 + i * (tile * Z + 20)
        board.paste(crop, (x0, y0))
        board.paste(crop.resize((tile * Z, tile * Z), Image.LANCZOS), (x0 + tile + 10, y0))
board.save(out, quality=90)
