"""Composes the city screen's map from the ukiyo-e isometric tile art (themes/ukiyo-e/hd/iso), in the game's diamond
layout: cell (x, y) is centred at ((x - y) * W / 2, (x + y) * H / 2). Drawn at 2x the game's 80x40 tile.
Usage: python citymap_iso.py OUT.png"""
import sys, random, math
import os
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "../../.."))
from PIL import Image, ImageDraw, ImageChops, ImageFilter
ISO = os.path.join(ROOT, 'themes/ukiyo-e/hd/iso/')
W, H = 160, 80
OX, OY = 320, 250                     # where the city's centre lands on the canvas
CW, CH = 640, 420
random.seed(7)

# rows are dy (north to south), columns dx (west to east), both -2..2; None is outside the city radius
T = [[None, 'fields', 'fields', 'palms', None],
     ['grass', 'grass', 'grass', 'palms', 'plains'],
     ['trees', 'grass', 'city', 'plains', 'plains'],
     ['hills', 'hills', 'plains', 'coast', 'sea'],
     [None, 'hills', 'coast', 'sea', None]]
RIVER = [(-3, -1), (-2, -1), (-1, -1), (0, -1), (1, -2), (2, -3)]          # (dx, dy) cell centres the river passes

def centre(dx, dy): return OX + (dx - dy) * W / 2, OY + (dx + dy) * H / 2
def diamond(dx, dy, grow=0):
    x, y = centre(dx, dy); w, h = W / 2 + grow, H / 2 + grow / 2
    return [(x, y - h), (x + w, y), (x, y + h), (x - w, y)]
def tinted(tex, mul):
    r, g, b = [c.point(lambda v, m=m: min(255, int(v * m))) for c, m in zip(tex.split(), mul)]
    return Image.merge('RGB', (r, g, b))

grass = Image.open(ISO + 'ground/grass.png').convert('RGB').resize((W * 2, W * 2))
sand = Image.open(ISO + 'ground/sand.png').convert('RGB').resize((W * 2, W * 2))
GROUND = {'grass': tinted(grass, (0.96, 1.06, 0.86)), 'fields': tinted(grass, (0.92, 1.1, 0.8)),
          'palms': tinted(grass, (0.95, 1.04, 0.85)), 'trees': tinted(grass, (0.9, 1.0, 0.85)),
          'plains': tinted(sand, (1.02, 0.98, 0.84)), 'hills': tinted(grass, (1.08, 1.0, 0.78)), 'city': tinted(sand, (1.0, 0.96, 0.86))}
SEA, SHALLOW = (40, 96, 124), (64, 132, 150)

canvas = Image.new('RGBA', (CW, CH), (0, 0, 0, 0))
land_mask = Image.new('L', (CW, CH), 0); sea_mask = Image.new('L', (CW, CH), 0); shallow_mask = Image.new('L', (CW, CH), 0)
ground = Image.new('RGB', (CW, CH), SEA)
for r, row in enumerate(T):
    for c, kind in enumerate(row):
        if not kind: continue
        dx, dy = c - 2, r - 2
        m = Image.new('L', (CW, CH), 0); ImageDraw.Draw(m).polygon(diamond(dx, dy, 1), fill=255)
        if kind in ('sea', 'coast'):
            (shallow_mask if kind == 'coast' else sea_mask).paste(255, mask=m)
            ground.paste(SHALLOW if kind == 'coast' else SEA, mask=m)
        else:
            tex = GROUND[kind]; tile = Image.new('RGB', (CW, CH))
            for tx in range(0, CW, tex.width):
                for ty in range(0, CH, tex.height): tile.paste(tex, (tx, ty))
            ground.paste(tile, mask=m); land_mask.paste(255, mask=m)
# soft shore: blur the land edge into the shallows, then a thin ink coastline
edge = land_mask.filter(ImageFilter.GaussianBlur(5))
foam = ImageChops.subtract(edge, land_mask).point(lambda v: min(255, v * 3))
ground.paste((214, 214, 190), mask=foam.point(lambda v: v // 2))
whole = ImageChops.lighter(ImageChops.lighter(land_mask, sea_mask), shallow_mask)
canvas.paste(ground, mask=whole)
d = ImageDraw.Draw(canvas)
coast = land_mask.filter(ImageFilter.FIND_EDGES).point(lambda v: 255 if v > 60 else 0)
coast = ImageChops.multiply(coast, ImageChops.lighter(sea_mask, shallow_mask).filter(ImageFilter.MaxFilter(5)))
canvas.paste((30, 34, 40, 255), mask=coast.point(lambda v: v * 3 // 4))
# wave marks on the sea
for _ in range(26):
    x, y = random.uniform(330, 600), random.uniform(300, 400)
    if sea_mask.getpixel((int(x), int(y))) > 200: d.arc([x - 10, y - 4, x + 10, y + 6], 200, 340, fill=(150, 190, 200, 255), width=2)

# fields: the painted crop plots laid into the field cells
plots = [Image.open(ISO + f'fieldplots/{i}.png').convert('RGB') for i in range(8)]
for r, row in enumerate(T):
    for c, kind in enumerate(row):
        if kind != 'fields': continue
        dx, dy = c - 2, r - 2
        m = Image.new('L', (CW, CH), 0); ImageDraw.Draw(m).polygon(diamond(dx, dy, -14), fill=255)
        layer = Image.new('RGB', (CW, CH))
        cx, cy = centre(dx, dy)
        for i in range(-4, 5):
            for j in range(-4, 5):
                p = plots[(i * 3 + j) % 8].resize((26, 26)); px, py = int(cx + (i - j) * 14 - 13), int(cy + (i + j) * 7 - 13)
                layer.paste(p, (px, py))
        canvas.paste(layer, mask=m)
        for k in range(-2, 3):   # dykes along the iso axes
            a = (cx + k * 14 - 28, cy + k * 7 + 14); b = (cx + k * 14 + 28, cy + k * 7 - 14)
            d.line([a, b], fill=(120, 140, 70, 255), width=1)

# river: a ribbon through the cell centres, ink bank then water
pts = [centre(x, y) for x, y in RIVER]
def smooth(p, n=12):
    out = []
    for i in range(len(p) - 1):
        (x0, y0), (x1, y1) = p[i], p[i + 1]
        for t in range(n):
            u = t / n; out.append((x0 + (x1 - x0) * u + math.sin(u * math.pi) * 6, y0 + (y1 - y0) * u))
    return out + [p[-1]]
rp = smooth(pts)
riv = Image.new('RGBA', (CW, CH), (0, 0, 0, 0)); rd = ImageDraw.Draw(riv)
rd.line(rp, fill=(30, 34, 40, 255), width=17, joint='curve')
rd.line(rp, fill=(214, 206, 170, 255), width=14, joint='curve')
rd.line(rp, fill=(58, 118, 160, 255), width=10, joint='curve')
riv.putalpha(ImageChops.multiply(riv.getchannel('A'), land_mask))
canvas.alpha_composite(riv)

# sprites, back to front
def load(folder, n): return [Image.open(ISO + f'{folder}/{i}.png').convert('RGBA') for i in range(n)]
SPR = {'trees': load('broadleaf', 16), 'palms': load('jungle', 12), 'hills': load('hills', 16), 'tufts': load('tufts', 25)}
items = []
def scatter(dx, dy, sprites, n, scale, spread=0.55):
    cx, cy = centre(dx, dy)
    for _ in range(n):
        u, v = random.uniform(-spread, spread), random.uniform(-spread, spread)
        x = cx + (u - v) * W / 2; y = cy + (u + v) * H / 2
        items.append((y, x, random.choice(sprites), scale))
for r, row in enumerate(T):
    for c, kind in enumerate(row):
        dx, dy = c - 2, r - 2
        if kind == 'trees': scatter(dx, dy, SPR['trees'], 7, 0.36)
        elif kind == 'palms': scatter(dx, dy, SPR['palms'], 5, 0.4)
        elif kind == 'hills': scatter(dx, dy, SPR['hills'], 2, 0.45, 0.3)
        elif kind in ('plains', 'grass'): scatter(dx, dy, SPR['tufts'], 3, 0.3)
town = Image.open(ISO + 'towns/ne/a0_s2_w.png').convert('RGBA')
cx, cy = centre(0, 0); items.append((cy + 22, cx, town, 0.46))
for y, x, im, s in sorted(items, key=lambda t: t[0]):
    sp = im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))))
    canvas.alpha_composite(sp, (int(x - sp.width / 2), int(y - sp.height + 6)))
canvas.save(sys.argv[1]); print('saved', canvas.size)
