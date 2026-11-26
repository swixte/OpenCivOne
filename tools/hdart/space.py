"""Builds the spaceship pictures for a theme from generated art (see themes/<theme>/space/prompts.json).

DOCKER: the spaceship's parts, cut from the sheet into the rectangles of the game's table (DS:4BE2, read from its
resources): two structure pieces, propulsion, fuel, an empty slot, habitation, life support, solar panel, a support
frame and the command capsule. PLANET2: the 24 frames (49x49 at (50 * (i % 6) + 1, 50 * (i // 6) + 1)) of the colony
ship coming down to land at Alpha Centauri, made from one painted ship so every frame is the same craft: it grows
as it nears, its retro-flame flickers, and in the last frames it settles with a puff of dust.

Usage: python3 space.py RAW_DIR THEME_HD_DIR
"""
import math
import sys

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

sys.path.insert(0, __import__('os').path.dirname(__file__))
from painted_assets import cut_object  # noqa: E402

S = 4
PARTS = [('structure_a', (0, 16, 16, 16)), ('structure_b', (16, 16, 16, 16)), ('propulsion', (32, 16, 16, 16)),
         ('fuel', (256, 48, 32, 16)), (None, (0, 80, 16, 16)), ('habitation', (192, 96, 32, 32)),
         ('lifesupport', (224, 96, 32, 32)), ('solar', (288, 96, 32, 32)), ('support', (0, 96, 32, 32)),
         ('capsule', (256, 96, 32, 32))]


def fit(obj, w, h, pad=0.06):
    k = min(w * (1 - pad) / obj.width, h * (1 - pad) / obj.height)
    obj = obj.resize((max(1, int(obj.width * k)), max(1, int(obj.height * k))), Image.LANCZOS)
    cell = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    cell.alpha_composite(obj, ((w - obj.width) // 2, (h - obj.height) // 2))
    return cell


def docker(raw, hd):
    sheet = Image.new('RGBA', (320 * S, 200 * S), (0, 0, 0, 0))
    for name, (x, y, w, h) in PARTS:
        if name is None:
            # the empty slot: a faint dashed outline where a part can go
            cell = Image.new('RGBA', (w * S, h * S), (0, 0, 0, 0))
            d = ImageDraw.Draw(cell)
            for i in range(0, w * S, 8):
                for a, b in (((i, 1), (i + 4, 1)), ((i, h * S - 2), (i + 4, h * S - 2)),
                             ((1, i), (1, i + 4)), ((w * S - 2, i), (w * S - 2, i + 4))):
                    d.line((a, b), fill=(150, 140, 110, 150), width=2)
        else:
            cell = fit(cut_object(Image.open(f'{raw}/{name}.png'), size=1024), w * S, h * S)
        sheet.alpha_composite(cell, (x * S, y * S))
    sheet.save(f'{hd}/DOCKER.png')


def planet(raw, hd):
    ship = cut_object(Image.open(f'{raw}/colonyship.png'), size=1024)
    flame_top = int(ship.height * 0.715)         # the flame is the bottom part of the painting
    sheet = Image.new('RGBA', (320 * S, 200 * S), (0, 0, 0, 0))
    F = 49 * S
    for i in range(24):
        t = i / 23
        land = min(1, t / 0.8)                   # it touches down at 80%, then settles
        ease = 1 - (1 - land) ** 2
        h = int(F * (0.42 + 0.5 * ease))
        k = h / ship.height
        frame = ship.copy()
        # the flame flickers while it burns and dies away once the ship is down
        burn = 1 if t < 0.8 else max(0, 1 - (t - 0.8) / 0.2)
        flick = 0.75 + 0.25 * math.sin(i * 2.3)
        flame = frame.crop((0, flame_top, frame.width, frame.height))
        fl = flame.resize((flame.width, max(1, int(flame.height * burn * flick))), Image.LANCZOS)
        frame = frame.crop((0, 0, frame.width, flame_top))
        tall = Image.new('RGBA', (frame.width, flame_top + flame.height), (0, 0, 0, 0))
        tall.alpha_composite(frame)
        if fl.height > 1:
            tall.alpha_composite(ImageEnhance.Brightness(fl).enhance(0.9 + 0.3 * flick), (0, flame_top))
        tall = tall.resize((max(1, int(tall.width * k)), max(1, int(tall.height * k))), Image.LANCZOS)
        cell = Image.new('RGBA', (F, F), (0, 0, 0, 0))
        base = int(F * (0.33 + 0.52 * ease))     # where the ship's foot is (the flame hangs below it)
        foot = int(flame_top * k)
        cx = F // 2 + int((1 - ease) * F * 0.12)
        cell.alpha_composite(tall, (cx - tall.width // 2, base - foot))
        if t >= 0.75:
            # dust thrown up where it lands
            dust = Image.new('RGBA', (F, F), (0, 0, 0, 0))
            d = ImageDraw.Draw(dust)
            r = int(F * (0.12 + 0.3 * (t - 0.75) / 0.25))
            a = int(170 * (1 - (t - 0.75) / 0.3))
            for dx in (-1, 1):
                d.ellipse((cx + dx * r // 2 - r // 2, base - r // 4, cx + dx * r // 2 + r // 2, base + r // 6), fill=(214, 196, 158, a))
            cell.alpha_composite(dust.filter(ImageFilter.GaussianBlur(3)))
        sheet.alpha_composite(cell, ((50 * (i % 6) + 1) * S, (50 * (i // 6) + 1) * S))
    sheet.save(f'{hd}/PLANET2.png')


if __name__ == '__main__':
    docker(sys.argv[1], sys.argv[2])
    planet(sys.argv[1], sys.argv[2])
    print('spaceship pictures written')
