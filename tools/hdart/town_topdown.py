"""Paints a theme's ladder of towns seen from straight above, as the gouache reference shows them: a central square,
streets running out from it and round it, blocks packed with terracotta and slate roofs lit from the top left, and for
walled towns a ring of stone wall with round towers; open towns sit among small fields.

Usage: python3 town_topdown.py OUT_DIR [--seed 1] [--size 512]
  writes t0_o.png ... t5_w.png (t = the ladder step: a hamlet, three steps of towns, a city, a capital)
"""

import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOFS = [(178, 78, 46), (192, 94, 54), (160, 68, 42), (198, 112, 66), (148, 74, 50), (174, 98, 62), (104, 98, 98)]
STONE = (206, 192, 160)
STREET = (168, 140, 98)
PLAZA = (226, 214, 186)


def shade_roof(draw, poly, colour, rng, ridge_axis):
    """A pitched roof from above: two faces, the one turned to the top left lighter, a thin ridge between them."""
    xs = [p[0] for p in poly]
    ys = [p[1] for p in poly]
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    jitter = rng.uniform(-10, 10)
    c = tuple(int(max(0, min(255, v + jitter))) for v in colour)
    light = tuple(int(min(255, v * 1.18 + 8)) for v in c)
    dark = tuple(int(v * 0.72) for v in c)
    if ridge_axis == 0:  # ridge runs left-right: top face lit
        mid = (y0 + y1) / 2
        draw.rectangle([x0, y0, x1, mid], fill=light)
        draw.rectangle([x0, mid, x1, y1], fill=dark)
        draw.line([x0, mid, x1, mid], fill=tuple(int(v * 0.55) for v in c), width=1)
    else:  # ridge runs up-down: left face lit
        mid = (x0 + x1) / 2
        draw.rectangle([x0, y0, mid, y1], fill=light)
        draw.rectangle([mid, y0, x1, y1], fill=dark)
        draw.line([mid, y0, mid, y1], fill=tuple(int(v * 0.55) for v in c), width=1)


def town(step, walled, size, rng):
    s = size
    cx = cy = s / 2
    # the town's radius on the tile grows with its step
    radius = s * (0.2, 0.31, 0.36, 0.41, 0.45, 0.47)[step]
    img = Image.new('RGBA', (s, s), (0, 0, 0, 0))
    shadow = Image.new('L', (s, s), 0)
    d = ImageDraw.Draw(img)
    sd = ImageDraw.Draw(shadow)

    # outskirts: a soft disc of trodden ground, and for open towns small fields around it
    ground = Image.new('L', (s, s), 0)
    ImageDraw.Draw(ground).ellipse([cx - radius * 1.12, cy - radius * 1.12, cx + radius * 1.12, cy + radius * 1.12], fill=255)
    ground = ground.filter(ImageFilter.GaussianBlur(s * 0.03))
    base = Image.new('RGBA', (s, s), (150, 132, 92, 0))
    base.putalpha(ground.point(lambda v: int(v * (0.85 if walled and step > 0 else 0.45))))
    img.alpha_composite(base)
    d = ImageDraw.Draw(img)

    if not walled or step == 0:
        for _ in range(10 + step * 4):
            a = rng.uniform(0, 2 * math.pi)
            r = radius * rng.uniform(1.0, 1.25)
            fx, fy = cx + math.cos(a) * r, cy + math.sin(a) * r
            w, h = rng.uniform(0.07, 0.13) * s, rng.uniform(0.04, 0.08) * s
            col = [(150, 150, 70), (176, 160, 80), (120, 136, 64), (186, 168, 96)][rng.integers(4)]
            d.rectangle([fx - w / 2, fy - h / 2, fx + w / 2, fy + h / 2], fill=col + (235,))
            for k in range(1, 5):  # furrows
                yy = fy - h / 2 + k * h / 5
                d.line([fx - w / 2, yy, fx + w / 2, yy], fill=tuple(int(v * 0.85) for v in col) + (200,), width=1)

    # the town's own ground: dark trodden earth between the houses, solid so the land under it does not show through
    d.ellipse([cx - radius * 0.97, cy - radius * 0.97, cx + radius * 0.97, cy + radius * 0.97], fill=(66, 54, 38, 255))

    # streets: radial lanes from the square and one ring
    lanes = 0 if step == 0 else 3 + step // 2
    plaza = radius * (0.16 if step < 4 else 0.2)
    street_w = max(2, int(s * 0.018))
    if step > 0:
        d.ellipse([cx - plaza, cy - plaza, cx + plaza, cy + plaza], fill=PLAZA + (255,))
    angles = [i * 2 * math.pi / lanes + rng.uniform(-0.15, 0.15) for i in range(lanes)]
    for a in angles:
        d.line([cx, cy, cx + math.cos(a) * radius * 1.02, cy + math.sin(a) * radius * 1.02], fill=STREET + (255,), width=street_w)
    ring_r = radius * 0.58
    if step >= 2:
        d.ellipse([cx - ring_r, cy - ring_r, cx + ring_r, cy + ring_r], outline=STREET + (255,), width=street_w)

    # houses: packed along the lanes and rings, avoiding the streets and the square
    # houses big enough to read when the town is drawn about 50 pixels across in the game
    house = s * (0.085 if step < 3 else 0.07)
    count = int((radius / house) ** 2 * 5) if step > 0 else 6
    placed = []
    for _ in range(count * 30):
        if len(placed) >= count:
            break
        a = rng.uniform(0, 2 * math.pi)
        r = math.sqrt(rng.uniform((plaza + house) ** 2 / radius ** 2, 0.92 ** 2)) * radius
        hx, hy = cx + math.cos(a) * r, cy + math.sin(a) * r
        # off the lanes
        near_lane = min([abs(math.sin(a - b)) * r for b in angles if math.cos(a - b) > 0] or [1e9])
        if near_lane < street_w * 0.8 + house * 0.35:
            continue
        if step >= 2 and abs(r - ring_r) < street_w + house * 0.5:
            continue
        w = house * rng.uniform(0.8, 1.5) * (1.4 if step == 0 else 1)
        h = house * rng.uniform(0.7, 1.1) * (1.4 if step == 0 else 1)
        if any(abs(hx - px) < (w + pw) / 2 - w * 0.18 and abs(hy - py) < (h + ph) / 2 - h * 0.18 for px, py, pw, ph in placed):
            continue
        placed.append((hx, hy, w, h))
    for hx, hy, w, h in placed:
        sd.rectangle([hx - w / 2 + 2, hy - h / 2 + 2, hx + w / 2 + 3, hy + h / 2 + 3], fill=150)
    img_shadow = Image.new('RGBA', (s, s), (20, 16, 10, 0))
    img_shadow.putalpha(shadow.filter(ImageFilter.GaussianBlur(1.2)).point(lambda v: int(v * 0.6)))
    img.alpha_composite(img_shadow)
    d = ImageDraw.Draw(img)
    for hx, hy, w, h in placed:
        colour = ROOFS[rng.integers(len(ROOFS))]
        shade_roof(d, [(hx - w / 2, hy - h / 2), (hx + w / 2, hy + h / 2)], colour, rng, 0 if w >= h else 1)

    # landmarks: a temple at the square for towns, a domed palace for the city, a marble palace for the capital
    if step >= 3:
        tw = radius * 0.22
        tx, ty = cx + plaza * 1.35, cy - plaza * 1.35
        d.rectangle([tx - tw / 2 + 3, ty - tw * 0.35 + 3, tx + tw / 2 + 3, ty + tw * 0.35 + 3], fill=(30, 24, 18, 120))
        shade_roof(d, [(tx - tw / 2, ty - tw * 0.35), (tx + tw / 2, ty + tw * 0.35)], (226, 214, 190), rng, 0)
    if step >= 4:
        dr = plaza * 0.62
        d.ellipse([cx - dr + 3, cy - dr + 3, cx + dr + 3, cy + dr + 3], fill=(30, 24, 18, 130))
        for k in range(12, 0, -1):  # a gilded dome, lit from the top left
            f = k / 12
            off = (1 - f) * dr * 0.45
            col = (int(150 + 100 * (1 - f)), int(110 + 90 * (1 - f)), int(40 + 60 * (1 - f)))
            d.ellipse([cx - dr * f - off, cy - dr * f - off, cx + dr * f - off, cy + dr * f - off], fill=col + (255,))
    if step == 5:
        pw = radius * 0.5
        for (ax, ay, bx, by) in ((-pw, -pw * 0.18, pw, pw * 0.18), (-pw * 0.18, -pw, pw * 0.18, pw)):
            d.rectangle([cx + ax, cy + ay, cx + bx, cy + by], outline=(240, 236, 226, 255), width=max(2, int(s * 0.008)))

    # the wall: a ring of pale stone with round towers, a gate on each main lane
    if walled and step > 0:
        wall_w = max(3, int(s * (0.016 + 0.003 * step)))
        wr = radius * 1.0
        d.ellipse([cx - wr + 3, cy - wr + 3, cx + wr + 3, cy + wr + 3], outline=(20, 16, 12, 140), width=wall_w + 2)
        d.ellipse([cx - wr, cy - wr, cx + wr, cy + wr], outline=STONE + (255,), width=wall_w)
        d.ellipse([cx - wr + wall_w * 0.5, cy - wr + wall_w * 0.5, cx + wr - wall_w * 0.5, cy + wr - wall_w * 0.5],
                  outline=tuple(int(v * 0.78) for v in STONE) + (255,), width=max(1, wall_w // 3))
        towers = 6 + step * 2
        tr = wall_w * 0.85
        for k in range(towers):
            a = k * 2 * math.pi / towers + 0.2
            tx, ty = cx + math.cos(a) * (wr - wall_w / 2), cy + math.sin(a) * (wr - wall_w / 2)
            d.ellipse([tx - tr + 2, ty - tr + 2, tx + tr + 2, ty + tr + 2], fill=(20, 16, 12, 150))
            d.ellipse([tx - tr, ty - tr, tx + tr, ty + tr], fill=STONE + (255,))
            d.ellipse([tx - tr * 0.75 - tr * 0.2, ty - tr * 0.75 - tr * 0.2, tx + tr * 0.55, ty + tr * 0.55], fill=(232, 222, 196, 255))

    return img


def paint(img, rng):
    """A painted finish: edges softened a touch, a light brush grain and broad variations of light, as gouache has."""
    a = np.asarray(img, np.float32)
    rgb, alpha = a[..., :3], a[..., 3:]
    h, w = rgb.shape[:2]
    soft = np.asarray(img.filter(ImageFilter.GaussianBlur(1.1)), np.float32)[..., :3]
    rgb = rgb * 0.65 + soft * 0.35
    broad = np.asarray(Image.fromarray((rng.random((h // 32, w // 32)) * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), np.float32) / 255 - 0.5
    grain = rng.normal(0, 1, (h, w)).astype(np.float32)
    grain = np.asarray(Image.fromarray(((grain * 40) + 128).clip(0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.8)), np.float32) / 128 - 1
    rgb = rgb * (1 + broad[..., None] * 0.12 + grain[..., None] * 0.05)
    return Image.fromarray(np.concatenate([rgb.clip(0, 255), alpha], -1).astype(np.uint8), 'RGBA')


def main():
    args = sys.argv[1:]
    seed, size = 1, 512
    if '--seed' in args:
        i = args.index('--seed'); seed = int(args[i + 1]); del args[i:i + 2]
    if '--size' in args:
        i = args.index('--size'); size = int(args[i + 1]); del args[i:i + 2]
    if len(args) != 1:
        sys.exit(__doc__)
    out = args[0]
    os.makedirs(out, exist_ok=True)
    for step in range(6):
        for walled in (False, True):
            rng = np.random.default_rng(seed * 100 + step)
            # paint large and scale down, for clean edges
            img = paint(town(step, walled, size * 2, rng), rng).resize((size, size), Image.LANCZOS)
            img.save(os.path.join(out, f't{step}_{"w" if walled else "o"}.png'))
    print('towns written to', out)


if __name__ == '__main__':
    main()
