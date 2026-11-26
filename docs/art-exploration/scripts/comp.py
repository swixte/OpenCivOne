import json, math, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageChops

RAW = '/tmp/mood-comp/raw'
WORK = '/tmp/mood-comp/work'
FINAL = '/opt/cursor/artifacts/moodboard/composite'
W, H = 1536, 1024
SIDE = 256
CELL = 128
COLS, ROWS = (W - SIDE) // CELL, H // CELL
FILL = 0.86
PLAYER = (30, 95, 235)
RIVAL = (215, 35, 35)
os.makedirs(WORK, exist_ok=True)
os.makedirs(FINAL, exist_ok=True)
rng = np.random.default_rng(7)


# ---------- sprites ----------
def key_out(img):
    a = np.asarray(img.convert('RGB'), dtype=np.float64)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    key = np.minimum(r, b) - g
    alpha = np.clip((150 - key) / 110, 0, 1)
    al = Image.fromarray(np.uint8(alpha * 255)).filter(ImageFilter.MinFilter(3))
    mask = al.point(lambda v: 255 if v > 128 else 0).filter(ImageFilter.MinFilter(5)).filter(ImageFilter.MaxFilter(9))
    alpha = np.asarray(ImageChops.multiply(al, mask), dtype=np.float64) / 255
    spill = np.clip(np.minimum(r, b) - g - 4, 0, None)
    a = a.copy()
    a[..., 0] -= spill
    a[..., 2] -= spill
    return np.concatenate([np.clip(a, 0, 255), alpha[..., None] * 255], -1)


def trim(rgba):
    al = rgba[..., 3] > 40
    rows = np.nonzero(al.sum(1) > 2)[0]
    cols = np.nonzero(al.sum(0) > 2)[0]
    return rgba[rows.min():rows.max() + 1, cols.min():cols.max() + 1]


def load_sprite(name, fit):
    path = f'{WORK}/{name}.png'
    if not os.path.exists(path):
        rgba = trim(key_out(Image.open(f'{RAW}/{name}.png')))
        Image.fromarray(np.rint(rgba).astype(np.uint8), 'RGBA').save(path)
    img = Image.open(path)
    k = fit / max(img.size)
    img = img.resize((max(1, round(img.width * k)), max(1, round(img.height * k))), Image.LANCZOS)
    rgb, a = img.convert('RGB'), img.getchannel('A')
    rgb = ImageEnhance.Color(rgb).enhance(1.18)
    rgb = ImageEnhance.Contrast(rgb).enhance(1.12)
    rgb = ImageEnhance.Brightness(rgb).enhance(1.05)
    rgb = rgb.filter(ImageFilter.UnsharpMask(1.0, 70, 2))
    out = rgb.convert('RGBA')
    out.putalpha(a)
    return out


def paste_sprite(canvas, spr, cx, cy, outline):
    x, y = round(cx - spr.width / 2), round(cy - spr.height / 2)
    a = spr.getchannel('A')
    pad = 12
    big = Image.new('L', (spr.width + 2 * pad, spr.height + 2 * pad), 0)
    big.paste(a, (pad, pad))
    shadow = big.filter(ImageFilter.GaussianBlur(4)).point(lambda v: int(v * 0.55))
    canvas.paste((10, 8, 5, 255), (x - pad + 5, y - pad + 6), shadow)
    ring = big.filter(ImageFilter.MaxFilter(5))
    canvas.paste(outline, (x - pad, y - pad), ring.point(lambda v: int(v * 0.8)))
    canvas.alpha_composite(spr, (x, y)) if canvas.mode == 'RGBA' else canvas.paste(spr, (x, y), spr)
    return x, y


def badge(colour, w=26, h=19, s=4):
    im = Image.new('RGBA', (w * s, h * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((0, 0, w * s - 1, h * s - 1), 3 * s, fill=(15, 12, 10, 255))
    d.rounded_rectangle((2 * s, 2 * s, w * s - 1 - 2 * s, h * s - 1 - 2 * s), 2 * s, fill=(250, 248, 240, 255))
    d.rounded_rectangle((4 * s, 4 * s, w * s - 1 - 4 * s, h * s - 1 - 4 * s), s, fill=colour + (255,))
    return im.resize((w, h), Image.LANCZOS)


def flag(colour, s=4):
    w, h = 22, 30
    im = Image.new('RGBA', (w * s, h * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rectangle((1 * s, 0, 4 * s, h * s), fill=(15, 12, 10, 255))
    d.rectangle((2 * s, 1 * s, 3 * s, h * s - s), fill=(235, 220, 170, 255))
    d.rectangle((3 * s, 1 * s, 21 * s, 14 * s), fill=(15, 12, 10, 255))
    d.rectangle((5 * s, 3 * s, 19 * s, 12 * s), fill=(250, 248, 240, 255))
    d.rectangle((7 * s, 5 * s, 17 * s, 10 * s), fill=colour + (255,))
    return im.resize((w, h), Image.LANCZOS)


# ---------- ground ----------
def grade(img, sat=0.85, con=0.9, bri=1.0):
    img = ImageEnhance.Color(img).enhance(sat)
    img = ImageEnhance.Contrast(img).enhance(con)
    return ImageEnhance.Brightness(img).enhance(bri)


# ---------- sidebar materials ----------
def noise(w, h, scale, seed):
    r = np.random.default_rng(seed)
    small = r.random((max(2, h // scale[1]), max(2, w // scale[0])))
    return np.asarray(Image.fromarray(np.uint8(small * 255)).resize((w, h), Image.BICUBIC), dtype=np.float64) / 255


def woodgrain(w, h, light, dark, period=9.0, warp=6.0, strength=0.6, seed=1):
    x = np.arange(w)[None, :].repeat(h, 0).astype(np.float64)
    wv = noise(w, h, (40, 160), seed) * warp + noise(w, h, (8, 50), seed + 1) * warp * 0.4
    v = 0.5 + 0.5 * np.sin(2 * np.pi * (x / period + wv))
    v = v ** 3 * strength + noise(w, h, (2, 12), seed + 2) * 0.25
    v = np.clip(v, 0, 1)[..., None]
    a = np.array(light)[None, None] * (1 - v) + np.array(dark)[None, None] * v
    return Image.fromarray(np.uint8(np.clip(a, 0, 255)))


def stone(w, h, base, seed=3):
    n = noise(w, h, (30, 30), seed) * 0.5 + noise(w, h, (8, 8), seed + 1) * 0.3 + noise(w, h, (2, 2), seed + 2) * 0.2
    a = np.array(base)[None, None] * (0.86 + 0.24 * n[..., None])
    return Image.fromarray(np.uint8(np.clip(a, 0, 255)))


def frame(d, box, style, width=None):
    x0, y0, x1, y1 = box
    if style == 'ukiyo-e':
        d.rectangle((x0 - 6, y0 - 6, x1 + 6, y1 + 6), outline=(18, 16, 14), width=2)
        d.rectangle((x0 - 4, y0 - 4, x1 + 4, y1 + 4), outline=(178, 52, 36), width=2)
        d.rectangle((x0 - 2, y0 - 2, x1 + 2, y1 + 2), outline=(18, 16, 14), width=2)
    elif style == 'classicism':
        cols = [(120, 108, 88), (246, 240, 226), (226, 216, 196), (168, 150, 122), (96, 84, 66)]
        for i, c in enumerate(cols):
            o = 7 - i * 1.5
            d.rectangle((x0 - o, y0 - o, x1 + o, y1 + o), outline=c, width=2)
    else:
        cols = [(60, 40, 14), (150, 108, 40), (236, 200, 110), (255, 236, 160), (196, 150, 62), (110, 76, 26), (40, 26, 10)]
        for i, c in enumerate(cols):
            d.rectangle((x0 - 8 + i, y0 - 8 + i, x1 + 8 - i, y1 + 8 - i), outline=c, width=1)
        d.rectangle((x0 - 1, y0 - 1, x1 + 1, y1 + 1), outline=(20, 12, 6), width=1)


def bevel_panel(img, box, style):
    """A recessed panel to hold the sidebar parts."""
    d = ImageDraw.Draw(img, 'RGBA')
    x0, y0, x1, y1 = box
    if style == 'ukiyo-e':
        d.rectangle(box, fill=(255, 250, 235, 70), outline=(18, 16, 14, 255), width=2)
    elif style == 'classicism':
        d.rectangle(box, fill=(0, 0, 0, 18))
        d.line((x0, y1, x0, y0, x1, y0), fill=(110, 98, 80, 255), width=3)
        d.line((x0, y1, x1, y1, x1, y0), fill=(252, 248, 238, 255), width=3)
    else:
        d.rectangle(box, fill=(0, 0, 0, 90))
        d.rectangle(box, outline=(196, 150, 62, 255), width=2)
        d.rectangle((x0 + 4, y0 + 4, x1 - 4, y1 - 4), outline=(110, 76, 26, 255), width=1)


def water_mask(img):
    """Sea and its surf: strongly blue pixels, plus bright foam next to them, cleaned of specks."""
    w, h = img.size
    sm = img.convert('RGB').resize((384, 256), Image.LANCZOS)
    a = np.asarray(sm, dtype=np.float64)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    blue = (b - r > 50) & (b > g + 12)
    bm = Image.fromarray(np.uint8(blue * 255)).filter(ImageFilter.MinFilter(3)).filter(ImageFilter.MaxFilter(3))
    near = np.asarray(bm.filter(ImageFilter.MaxFilter(9))) > 128
    foam = (a.mean(-1) > 175) & (a.max(-1) - a.min(-1) < 70) & near
    m = Image.fromarray(np.uint8((np.asarray(bm) > 128 | foam) * 255))
    m = Image.fromarray(np.uint8(((np.asarray(bm) > 128) | foam) * 255))
    m = m.filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.MinFilter(5)).filter(ImageFilter.MinFilter(3)).filter(ImageFilter.MaxFilter(3))
    return np.asarray(m.resize((w, h), Image.BILINEAR)) > 128


def minimap(plate, view, size=(224, 150)):
    """A small world: the plate (extended by mirroring, so it continues without seams) cut into continents, view rectangle on the plate's crop."""
    mw, mh = size
    sea_px = np.asarray(plate.convert('RGB'))[water_mask(plate)]
    deep = np.percentile(sea_px, 25, axis=0) if len(sea_px) else np.array([40, 70, 120])
    pw, ph = 78, 52
    small = plate.resize((pw, ph), Image.LANCZOS)
    T = Image.Transpose
    big = Image.new('RGB', (pw * 3, ph * 3))
    for i in range(3):
        for j in range(3):
            t = small
            if i != 1:
                t = t.transpose(T.FLIP_LEFT_RIGHT)
            if j != 1:
                t = t.transpose(T.FLIP_TOP_BOTTOM)
            big.paste(t, (i * pw, j * ph))
    land_big = ~water_mask(big)
    px, py = 40, 36
    yy, xx = np.mgrid[0:mh, 0:mw].astype(np.float64)
    worlds = []
    # (source rotation, source offset into world, blob centre, blob radii, seed)
    specs = [(0, (px - pw, py - ph), (px + pw * 0.45, py + ph * 0.5), (66, 46), 11),
             (180, (-150, -110), (182, 34), (30, 26), 12),
             (180, (-60, -60), (186, 122), (28, 20), 13),
             (90, (-40, -150), (24, 124), (18, 16), 14)]
    rgb = np.ones((mh, mw, 3)) * deep
    landmask = np.zeros((mh, mw), bool)
    for rot, (ox, oy), (cx, cy), (rx, ry), seed in specs:
        src = big.rotate(rot, expand=True)
        lb = Image.fromarray(np.uint8(land_big * 255)).rotate(rot, expand=True)
        canvas = Image.new('RGB', size, tuple(int(v) for v in deep))
        canvas.paste(src, (ox, oy))
        lm = Image.new('L', size, 0)
        lm.paste(lb, (ox, oy))
        n = noise(mw, mh, (10, 10), seed) * 0.5 + noise(mw, mh, (4, 4), seed + 1) * 0.25
        r = np.sqrt(((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2) + (n - 0.37) * 0.9
        m = (np.asarray(lm) > 128) & (r < 1.0)
        rgb[m] = np.asarray(canvas)[m]
        landmask |= m
    shallow = np.clip(deep * 1.25 + 30, 0, 255)
    ring = np.asarray(Image.fromarray(np.uint8(landmask * 255)).filter(ImageFilter.MaxFilter(3))) > 128
    rgb[ring & ~landmask] = shallow
    world = Image.fromarray(np.uint8(rgb))
    vx0, vy0, vx1, vy1 = view
    k = pw / plate.width
    d = ImageDraw.Draw(world)
    rect = (px + vx0 * k, py + vy0 * k, px + vx1 * k, py + vy1 * k)
    d.rectangle(rect, outline=(10, 10, 10), width=3)
    d.rectangle(rect, outline=(255, 255, 255), width=1)
    return world


def sidebar(style, plate, view, portrait, unit_spr, unit_ground):
    if style == 'ukiyo-e':
        img = woodgrain(SIDE, H, (240, 229, 202), (214, 196, 160), period=11, warp=5, strength=0.45)
    elif style == 'classicism':
        img = stone(SIDE, H, (222, 214, 196))
    else:
        img = woodgrain(SIDE, H, (78, 48, 28), (30, 17, 9), period=8, warp=7, strength=0.8)
    d = ImageDraw.Draw(img)
    # minimap
    mm = minimap(plate, view)
    bevel_panel(img, (8, 8, SIDE - 9, 186), style)
    img.paste(mm, (16, 22))
    frame(d, (16, 22, 16 + mm.width - 1, 22 + mm.height - 1), style)
    # portrait
    bevel_panel(img, (8, 200, SIDE - 9, 470), style)
    pw, ph = 180, 236
    p = portrait
    k = max(pw / p.width, ph / p.height) * 1.12
    p = p.resize((round(p.width * k), round(p.height * k)), Image.LANCZOS)
    left = (p.width - pw) // 2
    top = int((p.height - ph) * 0.25)
    p = p.crop((left, top, left + pw, top + ph))
    px, py = (SIDE - pw) // 2, 216
    img.paste(p, (px, py))
    frame(d, (px, py, px + pw - 1, py + ph - 1), style)
    # four bars
    bevel_panel(img, (8, 484, SIDE - 9, 700), style)
    bar_cols = [(232, 182, 40), (196, 24, 44), (40, 96, 220), (52, 160, 64)]
    levels = [0.72, 0.46, 0.84, 0.33]
    track = {'ukiyo-e': (60, 52, 44), 'classicism': (120, 110, 94), 'baroque': (18, 10, 6)}[style]
    for i, (c, lv) in enumerate(zip(bar_cols, levels)):
        y = 508 + i * 48
        x0, x1 = 30, SIDE - 31
        d.rectangle((x0, y, x1, y + 20), fill=track)
        fx = x0 + round((x1 - x0) * lv)
        for yy in range(y, y + 21):
            t = (yy - y) / 20
            f = 1.25 - 0.5 * t
            d.line((x0, yy, fx, yy), fill=tuple(min(255, int(v * f)) for v in c))
        frame(d, (x0, y, x1, y + 20), style)
    # active unit panel
    bevel_panel(img, (8, 714, SIDE - 9, H - 9), style)
    box = 168
    ug = unit_ground.resize((box, box), Image.LANCZOS)
    k = box * 0.86 / max(unit_spr.size)
    us = unit_spr.resize((round(unit_spr.width * k), round(unit_spr.height * k)), Image.LANCZOS)
    tile = ug.convert('RGBA')
    tile.alpha_composite(us, ((box - us.width) // 2, (box - us.height) // 2))
    ux, uy = (SIDE - box) // 2, 736
    img.paste(tile.convert('RGB'), (ux, uy))
    frame(d, (ux, uy, ux + box - 1, uy + box - 1), style)
    # move pips
    pip_on = {'ukiyo-e': (178, 52, 36), 'classicism': (176, 84, 52), 'baroque': (236, 200, 110)}[style]
    pip_off = {'ukiyo-e': (214, 196, 160), 'classicism': (190, 180, 160), 'baroque': (60, 40, 20)}[style]
    edge = {'ukiyo-e': (18, 16, 14), 'classicism': (96, 84, 66), 'baroque': (20, 12, 6)}[style]
    for i in range(3):
        cx = SIDE // 2 + (i - 1) * 40
        cy = 950
        d.ellipse((cx - 12, cy - 12, cx + 12, cy + 12), fill=edge)
        d.ellipse((cx - 9, cy - 9, cx + 9, cy + 9), fill=pip_on if i < 2 else pip_off)
    # edge between sidebar and map
    if style == 'ukiyo-e':
        d.rectangle((SIDE - 5, 0, SIDE - 1, H), fill=(18, 16, 14))
        d.rectangle((SIDE - 4, 0, SIDE - 3, H), fill=(178, 52, 36))
    elif style == 'classicism':
        d.rectangle((SIDE - 6, 0, SIDE - 1, H), fill=(168, 150, 122))
        d.line((SIDE - 6, 0, SIDE - 6, H), fill=(250, 246, 236), width=2)
        d.line((SIDE - 1, 0, SIDE - 1, H), fill=(96, 84, 66), width=2)
    else:
        for i, c in enumerate([(40, 26, 10), (150, 108, 40), (255, 236, 160), (196, 150, 62), (60, 40, 14)]):
            d.line((SIDE - 5 + i, 0, SIDE - 5 + i, H), fill=c)
    return img


# ---------- map ----------
def build(style, cfg):
    plate = Image.open(f'{RAW}/{style}-ground.png').convert('RGB')
    cx0, cy0, cw = cfg.get('crop', (128, 0, 1280))
    ch = cw * (H / (W - SIDE))
    view = (cx0, cy0, cx0 + cw, cy0 + ch)
    ground = plate.crop(tuple(round(v) for v in view)).resize((W - SIDE, H), Image.LANCZOS)
    ground = grade(ground, *cfg.get('grade', (0.85, 0.9, 1.0)))
    if cfg.get('paper'):
        tex = woodgrain(H, W - SIDE, (255, 252, 242), (226, 210, 176), period=15, warp=9, strength=0.55, seed=21).rotate(90, expand=True)
        ground = ImageChops.multiply(ground, tex)
    mapimg = ground.convert('RGBA')
    outline = tuple(cfg.get('outline', (20, 14, 8))) + (255,)
    placed = []
    for item in cfg['items']:
        name, col, row, owner = item[:4]
        fit = CELL * FILL * (item[4] if len(item) > 4 else 1.0)
        spr = load_sprite(f'{style}-{name}', fit)
        cx, cy = col * CELL + CELL / 2, row * CELL + CELL / 2
        x, y = paste_sprite(mapimg, spr, cx, cy, outline)
        colour = PLAYER if owner == 'p' else RIVAL
        if name in ('castle', 'town'):
            b = badge(colour)
            mapimg.alpha_composite(b, (col * CELL + 6, row * CELL + 6))
        else:
            f = flag(colour)
            mapimg.alpha_composite(f, (col * CELL + 7, row * CELL + 6))
        placed.append((name, col, row, x, y, spr.width, spr.height))
    sc, sr = cfg['selected']
    d = ImageDraw.Draw(mapimg)
    x0, y0 = sc * CELL, sr * CELL
    d.rectangle((x0 + 1, y0 + 1, x0 + CELL - 2, y0 + CELL - 2), outline=(20, 16, 0), width=5)
    d.rectangle((x0 + 2, y0 + 2, x0 + CELL - 3, y0 + CELL - 3), outline=(255, 226, 0), width=3)
    # sidebar
    portrait = Image.open(f'{RAW}/{style}-portrait.png').convert('RGB')
    sel_name = next(it[0] for it in cfg['items'] if (it[1], it[2]) == (sc, sr))
    unit_spr = Image.open(f'{WORK}/{style}-{sel_name}.png')
    unit_ground = ground.crop((x0, y0, x0 + CELL, y0 + CELL))
    side = sidebar(style, plate, view, portrait, unit_spr, unit_ground)
    full = Image.new('RGB', (W, H))
    full.paste(side, (0, 0))
    full.paste(mapimg.convert('RGB'), (SIDE, 0))
    full.save(f'{FINAL}/{style}.jpg', quality=93)
    # grid proof
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
    full_g = full.copy()
    full_g.paste(g.convert('RGB'), (SIDE, 0))
    full_g.save(f'{FINAL}/{style}-grid.jpg', quality=93)
    for name, col, row, x, y, w, h in placed:
        assert col * CELL <= x and x + w <= (col + 1) * CELL and row * CELL <= y and y + h <= (row + 1) * CELL, (style, name)
    print(style, 'ok', placed)


def plate_grid(style, crop=(128, 0, 1280)):
    """The plate cropped to the map, with the cell grid and numbers, for choosing cells."""
    plate = Image.open(f'{RAW}/{style}-ground.png').convert('RGB')
    cx0, cy0, cw = crop
    ch = cw * (H / (W - SIDE))
    g = plate.crop((round(cx0), round(cy0), round(cx0 + cw), round(cy0 + ch))).resize((W - SIDE, H)).convert('RGBA')
    d = ImageDraw.Draw(g)
    for c in range(COLS):
        for r in range(ROWS):
            d.rectangle((c * CELL, r * CELL, (c + 1) * CELL - 1, (r + 1) * CELL - 1), outline=(255, 255, 0), width=1)
            d.text((c * CELL + 4, r * CELL + 2), f'{c},{r}', fill=(255, 255, 0))
    g.convert('RGB').save(f'{WORK}/{style}-plategrid.jpg', quality=85)


if __name__ == '__main__':
    cfgs = json.load(open('/tmp/mood-comp/layout.json'))
    for s in sys.argv[1:] or cfgs:
        build(s, cfgs[s])
