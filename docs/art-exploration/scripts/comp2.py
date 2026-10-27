import os, sys
from PIL import Image, ImageDraw, ImageFont, ImageChops
import comp
from comp import CELL, SIDE, W, H, COLS, ROWS, PLAYER, RIVAL

OUT = '/opt/cursor/artifacts/moodboard/ukiyo-units'
os.makedirs(OUT, exist_ok=True)
STYLE = 'ukiyo-e'
SELECTED = (4, 5)
ITEMS = [  # name, col, row, owner
    ('castle', 5, 3, 'p'), ('town', 3, 6, 'p'), ('town', 7, 1, 'r'), ('ship', 7, 5, 'p'),
    ('rider', 4, 5, 'p'), ('settler', 5, 6, 'p'), ('foot', 6, 1, 'r'), ('foot', 1, 5, 'p')]
FIT = {'castle': 0.82, 'town': 0.76, 'rider': 0.62, 'settler': 0.62, 'ship': 0.62, 'foot': 0.64}
CITIES = ('castle', 'town')

VARIANTS = {
    'current-smaller': dict(units='ukiyo-e-{n}', cities='ukiyo-e-{n}', flags=True, outline=(12, 10, 8)),
    'option-a-print': dict(units='ukiyo-e-A-{n}', cities='ukiyo-e-A-{n}', flags=True, outline=(12, 10, 8)),
    'option-b-token': dict(units='ukiyo-e-B-{n}', cities='ukiyo-e-{n}', flags=False, outline=(12, 10, 8), rival_suffix='-rival', unit_fit=0.65),
    'option-c-ink': dict(units='ukiyo-e-C-{n}', cities='ukiyo-e-C-{n}', flags=False, outline=(240, 230, 204), rival_suffix='-rival'),
}


def ground_and_plate():
    plate = Image.open(f'{comp.RAW}/{STYLE}-ground.png').convert('RGB')
    view = (128, 0, 1408, 1024)
    ground = plate.crop(view).resize((W - SIDE, H), Image.LANCZOS)
    ground = comp.grade(ground, 0.82, 0.9, 1.04)
    tex = comp.woodgrain(H, W - SIDE, (255, 252, 242), (226, 210, 176), period=15, warp=9, strength=0.55, seed=21).rotate(90, expand=True)
    return ImageChops.multiply(ground, tex), plate, view


def sprite_name(cfg, name, owner):
    tmpl = cfg['cities'] if name in CITIES else cfg['units']
    n = tmpl.format(n=name)
    if owner == 'r' and name not in CITIES and cfg.get('rival_suffix') and os.path.exists(f'{comp.RAW}/{n}{cfg["rival_suffix"]}.png'):
        n += cfg['rival_suffix']
    return n


def build(variant, cfg, ground, plate, view):
    mapimg = ground.convert('RGBA')
    outline = tuple(cfg['outline']) + (255,)
    placed = []
    sel_sprite = None
    for name, col, row, owner in ITEMS:
        n = sprite_name(cfg, name, owner)
        fit = FIT[name] if name in CITIES or 'unit_fit' not in cfg else cfg['unit_fit']
        spr = comp.load_sprite(n, CELL * fit)
        cx, cy = col * CELL + CELL / 2, row * CELL + CELL / 2
        x, y = comp.paste_sprite(mapimg, spr, cx, cy, outline)
        colour = PLAYER if owner == 'p' else RIVAL
        boxes = [(x, y, x + spr.width, y + spr.height)]
        mark = None
        if name in CITIES:
            mark = comp.badge(colour).resize((32, 24), Image.LANCZOS)
            mx, my = x - 10, y - 4
        elif cfg['flags']:
            mark = comp.flag(colour).resize((26, 36), Image.LANCZOS)
            mx, my = x - 14, y - 10
        if mark:
            mx = min(max(mx, col * CELL + 3), (col + 1) * CELL - 3 - mark.width)
            my = min(max(my, row * CELL + 3), (row + 1) * CELL - 3 - mark.height)
            mapimg.alpha_composite(mark, (mx, my))
            boxes.append((mx, my, mx + mark.width, my + mark.height))
        placed.append((n, col, row, boxes, spr.size))
        if (col, row) == SELECTED:
            sel_sprite = n
    sc, sr = SELECTED
    d = ImageDraw.Draw(mapimg)
    x0, y0 = sc * CELL, sr * CELL
    d.rectangle((x0 + 1, y0 + 1, x0 + CELL - 2, y0 + CELL - 2), outline=(20, 16, 0), width=5)
    d.rectangle((x0 + 2, y0 + 2, x0 + CELL - 3, y0 + CELL - 3), outline=(255, 226, 0), width=3)
    for n, col, row, boxes, size in placed:
        for bx0, by0, bx1, by1 in boxes:
            assert col * CELL <= bx0 and bx1 <= (col + 1) * CELL and row * CELL <= by0 and by1 <= (row + 1) * CELL, (variant, n)
    portrait = Image.open(f'{comp.RAW}/{STYLE}-portrait.png').convert('RGB')
    unit_spr = Image.open(f'{comp.WORK}/{sel_sprite}.png')
    side = comp.sidebar(STYLE, plate, view, portrait, unit_spr, ground.crop((x0, y0, x0 + CELL, y0 + CELL)))
    full = Image.new('RGB', (W, H))
    full.paste(side, (0, 0))
    full.paste(mapimg.convert('RGB'), (SIDE, 0))
    full.save(f'{OUT}/{variant}.jpg', quality=93)
    g = with_grid(mapimg)
    full.paste(g.convert('RGB'), (SIDE, 0))
    full.save(f'{OUT}/{variant}-grid.jpg', quality=93)
    print(variant, 'ok', [(p[0], p[4]) for p in placed])
    return mapimg.convert('RGB'), g.convert('RGB')


def with_grid(mapimg):
    g = mapimg.copy()
    ov = Image.new('RGBA', g.size, (0, 0, 0, 0))
    od = ImageDraw.Draw(ov)
    for c in range(1, COLS):
        od.line((c * CELL, 0, c * CELL, H), fill=(0, 0, 0, 120), width=3)
        od.line((c * CELL, 0, c * CELL, H), fill=(255, 255, 255, 215), width=1)
    for r in range(1, ROWS):
        od.line((0, r * CELL, W, r * CELL), fill=(0, 0, 0, 120), width=3)
        od.line((0, r * CELL, W, r * CELL), fill=(255, 255, 255, 215), width=1)
    g.alpha_composite(ov)
    return g


def compare(maps, name, region=(3, 5, 6, 7), k=1.5):
    c0, r0, c1, r1 = region
    pw, ph = round((c1 - c0) * CELL * k), round((r1 - r0) * CELL * k)
    pad, lab = 24, 56
    sheet = Image.new('RGB', (2 * pw + 3 * pad, 2 * (ph + lab) + 3 * pad), (240, 229, 202))
    d = ImageDraw.Draw(sheet)
    font = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf', 34)
    for i, (label, m) in enumerate(maps):
        crop = m.crop((c0 * CELL, r0 * CELL, c1 * CELL, r1 * CELL)).resize((pw, ph), Image.LANCZOS)
        x = pad + (i % 2) * (pw + pad)
        y = pad + (i // 2) * (ph + lab + pad)
        d.text((x, y + 6), label, fill=(18, 16, 14), font=font)
        sheet.paste(crop, (x, y + lab))
        d.rectangle((x - 2, y + lab - 2, x + pw + 1, y + lab + ph + 1), outline=(18, 16, 14), width=2)
    sheet.save(f'{OUT}/{name}.jpg', quality=93)


if __name__ == '__main__':
    ground, plate, view = ground_and_plate()
    sel = sys.argv[1:] or list(VARIANTS)
    res = {v: build(v, VARIANTS[v], ground, plate, view) for v in sel}
    if len(res) == 4:
        labels = ['Current', 'A', 'B', 'C']
        compare([(l, res[v][0]) for l, v in zip(labels, VARIANTS)], 'compare')
        compare([(l, res[v][1]) for l, v in zip(labels, VARIANTS)], 'compare-grid')
