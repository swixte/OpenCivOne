"""Ukiyo-v2 composites: improvements painted into the ground, cities that dominate units."""
import math, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageChops, ImageFont

sys.path.insert(0, '/tmp/mood-comp')
import comp, comp2, states
from states import (
    CELL, MW, MH, BLUE, RED, CREAM, INK, GOLD, SANS, SERIF,
    keyed, recolour, centre, shadow_layer, outline_layer, city_badge, capital_mon,
    count_seal, UNITS, CITIES, AIR, FLAGGED, faded, dimmed, sashimono, star,
    yellow_square, fog, goto_path, brush_frame,
)

OUT = '/opt/cursor/artifacts/moodboard/ukiyo-v2'
WORK = '/tmp/mood-comp/work/v2'
os.makedirs(OUT, exist_ok=True)
os.makedirs(WORK, exist_ok=True)

CITY_UP = 32
CITY_FILL = 0.95
UNIT_FIT = 0.62
SHIP_FIT = 0.64
AIR_FIT = 0.58
OBJ_FIT = 0.42
SHIPS = {'ship', 'trireme', 'battleship', 'transport'}
PAPER = (240, 229, 202)
_tex = {}
_spr = {}


def wood():
    return comp.woodgrain(MH, MW, (255, 252, 242), (226, 210, 176), period=15, warp=9, strength=0.45, seed=21).rotate(90, expand=True)


GRAIN = wood()


def texture(name, tile, flip=False, ox=0, oy=0):
    """Repeat a ground print across the map. Never mirror-tile: flips made paddies into a kaleidoscope."""
    key = (name, tile, flip, ox, oy)
    if key not in _tex:
        src = Image.open(f'{comp.RAW}/{name}.png').convert('RGB')
        t = src.resize((tile, tile), Image.LANCZOS)
        t = ImageEnhance.Color(t).enhance(1.06)
        t = ImageEnhance.Contrast(t).enhance(1.10)
        big = Image.new('RGB', (MW + tile, MH + tile))
        for i in range(-tile, MW + tile, tile):
            for j in range(-tile, MH + tile, tile):
                big.paste(t, (i, j))
        big = big.crop((ox % tile, oy % tile, ox % tile + MW, oy % tile + MH))
        big = ImageChops.multiply(big, GRAIN.resize((MW, MH)))
        _tex[key] = big
    return _tex[key]


def cells_mask(cells, inset=5, wobble=2.4, seed=1, soft=1.8):
    m = Image.new('L', (MW, MH), 0)
    d = ImageDraw.Draw(m)
    for c, r in cells:
        d.rectangle((c * CELL, r * CELL, (c + 1) * CELL - 1, (r + 1) * CELL - 1), fill=255)
    m = m.filter(ImageFilter.MinFilter(2 * inset + 1))
    b = np.asarray(m.filter(ImageFilter.GaussianBlur(4)), dtype=np.float64) / 255
    n = comp.noise(MW, MH, (7, 7), seed) * 0.55 + comp.noise(MW, MH, (3, 3), seed + 1) * 0.45
    t = b + (n - 0.5) * wobble / 10
    a = np.clip((t - 0.42) * 3.4 + 0.5, 0, 1)
    img = Image.fromarray(np.uint8(a * 255)).filter(ImageFilter.GaussianBlur(soft))
    hard = Image.new('L', (MW, MH), 0)
    hd = ImageDraw.Draw(hard)
    for c, r in cells:
        hd.rectangle((c * CELL + 1, r * CELL + 1, (c + 1) * CELL - 2, (r + 1) * CELL - 2), fill=255)
    hard = hard.filter(ImageFilter.MaxFilter(3))
    return ImageChops.multiply(img, hard)


def blend_in(canvas, img, mask, keyline=0.28):
    base = canvas.convert('RGB')
    out = Image.composite(img.convert('RGB'), base, mask)
    if keyline:
        a = np.asarray(mask, dtype=np.float64) / 255
        ring = np.exp(-((a - 0.55) / 0.16) ** 2)
        o = np.asarray(out, dtype=np.float64) * (1 - ring[..., None] * keyline) + np.array(INK) * ring[..., None] * keyline
        out = Image.fromarray(np.uint8(np.clip(o, 0, 255)))
    canvas.paste(out.convert('RGBA'))


def _paddy_tex_name():
    for n in ('ukiyo-e-T-paddy3', 'ukiyo-e-T-paddy4', 'ukiyo-e-T-paddy2', 'ukiyo-e-T-paddy'):
        if os.path.exists(f'{comp.RAW}/{n}.png'):
            return n
    return 'ukiyo-e-T-paddy'


def paddy_field(canvas, cells, seed=1):
    """Hiroshige paddies painted into the ground. Adjacent cells share one voronoi so bunds join."""
    if not cells:
        return
    cellset = set(map(tuple, cells))
    cs, rs = zip(*cells)
    c0, c1, r0, r1 = min(cs), max(cs) + 1, min(rs), max(rs) + 1
    x0, y0 = c0 * CELL, r0 * CELL
    w, h = (c1 - c0) * CELL, (r1 - r0) * CELL
    rng = np.random.default_rng(seed + c0 * 19 + r0 * 7)

    # ~2x2 sites per cell, jittered, so each cell holds a handful of irregular plots
    sites = []
    for c, r in cells:
        for i in range(2):
            for j in range(2):
                sx = (c - c0) * CELL + (i + 0.5) * (CELL / 2) + rng.normal(0, 11)
                sy = (r - r0) * CELL + (j + 0.5) * (CELL / 2) + rng.normal(0, 11)
                sites.append((sx, sy))
    sites = np.array(sites, dtype=np.float64)
    nsite = len(sites)
    kinds = rng.integers(0, 10, nsite)  # 0-5 water, 6-7 rice, 8-9 gold

    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
    d2 = (xx[..., None] - sites[:, 0]) ** 2 + (yy[..., None] - sites[:, 1]) ** 2
    pid = np.argmin(d2, axis=-1).astype(np.int32)

    inside = np.zeros((h, w), dtype=bool)
    for c, r in cells:
        inside[(r - r0) * CELL:(r - r0 + 1) * CELL, (c - c0) * CELL:(c - c0 + 1) * CELL] = True

    bund = np.zeros((h, w), dtype=bool)
    bund[:, 1:] |= pid[:, 1:] != pid[:, :-1]
    bund[1:, :] |= pid[1:, :] != pid[:-1, :]
    bund &= inside
    bund_i = Image.fromarray(np.uint8(bund) * 255).filter(ImageFilter.MaxFilter(3))
    bund_a = np.asarray(bund_i, dtype=np.float64) / 255
    bund_ink = np.asarray(bund_i.filter(ImageFilter.MaxFilter(3)), dtype=np.float64) / 255

    WATER_HI = np.array([232, 236, 220], dtype=np.float64)
    WATER_LO = np.array([118, 172, 206], dtype=np.float64)
    WATER_MID = np.array([168, 204, 214], dtype=np.float64)
    RICE = np.array([154, 176, 108], dtype=np.float64)
    RICE_DK = np.array([72, 108, 52], dtype=np.float64)
    GOLD = np.array([204, 168, 72], dtype=np.float64)
    GOLD_DK = np.array([158, 120, 44], dtype=np.float64)
    BUND = np.array([148, 122, 74], dtype=np.float64)
    BUND_DK = np.array([96, 74, 42], dtype=np.float64)

    # sky bokashi shared across the farm (print-like, not per-plot lighting)
    t = np.clip(yy / max(h, 1), 0, 1)
    cloud = comp.noise(w, h, (5, 4), seed + 3)
    water = WATER_HI * (1 - t[..., None]) * 0.55 + WATER_MID * (1 - t[..., None]) * 0.25 + WATER_LO * t[..., None]
    water = water * (0.88 + 0.18 * cloud[..., None]) + np.array([248, 246, 232]) * np.clip((cloud - 0.62) * 2.2, 0, 1)[..., None]

    # rice / gold hatch from fine noise
    hatch = comp.noise(w, h, (1, 3), seed + 5)
    rice = RICE * (0.82 + 0.25 * hatch[..., None]) + RICE_DK * np.clip((hatch - 0.55) * 1.8, 0, 1)[..., None]
    gold = GOLD * (0.85 + 0.22 * hatch[..., None]) + GOLD_DK * np.clip((hatch - 0.58) * 1.6, 0, 1)[..., None]

    fill = water.copy()
    kmap = kinds[pid]
    rice_m = (kmap >= 6) & (kmap <= 7)
    gold_m = kmap >= 8
    fill[rice_m] = rice[rice_m]
    fill[gold_m] = gold[gold_m]

    # earth bunds with a dark ink rim
    n_b = comp.noise(w, h, (3, 3), seed + 8)
    earth = BUND * (0.85 + 0.3 * n_b[..., None]) + BUND_DK * (1 - n_b[..., None]) * 0.25
    fill = fill * (1 - bund_a[..., None]) + earth * bund_a[..., None]
    ink_ring = np.clip(bund_ink - bund_a, 0, 1) * 0.78
    fill = fill * (1 - ink_ring[..., None]) + np.array(INK, dtype=np.float64) * ink_ring[..., None]

    grain = np.asarray(GRAIN.crop((x0, y0, x0 + w, y0 + h)).resize((w, h)), dtype=np.float64) / 255
    fill = fill * (0.78 + 0.22 * grain)

    # soft printed edge: union of cells, inset + wobble, never a hard sticker rectangle
    m = cells_mask(cells, inset=6, wobble=3.2, seed=seed, soft=2.4)
    m_c = np.asarray(m.crop((x0, y0, x0 + w, y0 + h)), dtype=np.float64) / 255
    m_c *= inside.astype(np.float64)
    # keep a 2px gap from the true cell edge so the grid still reads
    edge = np.zeros((h, w), dtype=np.float64)
    for c, r in cells:
        ly, lx = (r - r0) * CELL, (c - c0) * CELL
        edge[ly + 2:ly + CELL - 2, lx + 2:lx + CELL - 2] = 1
    m_c *= edge

    dest = np.asarray(canvas.crop((x0, y0, x0 + w, y0 + h)).convert('RGB'), dtype=np.float64)
    # slight multiply with the grass at the rim so paddies feel dyed into the paper
    rim = m_c * (1 - m_c) * 4
    rim = np.clip(rim, 0, 1)
    mixed = fill * (0.92 + 0.08 * (dest / 255.0))
    out = dest * (1 - m_c[..., None]) + mixed * m_c[..., None]
    out = out * (1 - rim[..., None] * 0.12) + np.array(INK, dtype=np.float64) * rim[..., None] * 0.12
    patch = Image.fromarray(np.uint8(np.clip(out, 0, 255))).convert('RGBA')
    canvas.paste(patch, (x0, y0))


def farmland(canvas, cells, kind='paddy', tile=None, seed=1):
    """Paint farmed land into the ground. Adjacent cells share one print so bunds join."""
    if not cells:
        return
    paddy_like = kind in ('paddy', 'ukiyo-e-T-paddy', 'ukiyo-e-T-paddy2', 'ukiyo-e-T-paddy3', 'ukiyo-e-T-paddy4') or 'paddy' in str(kind)
    if paddy_like:
        name = kind if str(kind).startswith('ukiyo-e-') and os.path.exists(f'{comp.RAW}/{kind}.png') else _paddy_tex_name()
        # paddy3 is ~5 plots across 1024px. Tile ~540 so a cell holds ~1 plot, three cells join into a farm.
        m = cells_mask(cells, inset=5, wobble=2.6, seed=seed, soft=2.0)
        tex = texture(name, tile or 500, ox=seed * 17, oy=seed * 11)
        blend_in(canvas, tex, m, keyline=0.20)
        return
    name = kind if os.path.exists(f'{comp.RAW}/{kind}.png') else 'ukiyo-e-T-fields'
    # fields texture is ~16 plots across 1024px; at tile=1024 each plot is 64px (half a cell)
    m = cells_mask(cells, inset=6, wobble=3.0, seed=seed, soft=2.6)
    tex = texture(name, tile or 1024, ox=seed * 13, oy=seed * 9)
    blend_in(canvas, tex, m, keyline=0.16)


def _wobbly(pts, amp, seed, step=9):
    r = np.random.default_rng(seed)
    out = []
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        L = math.hypot(x1 - x0, y1 - y0) or 1
        n = max(2, int(L // step))
        px, py = -(y1 - y0) / L, (x1 - x0) / L
        for k in range(n):
            t = k / n
            w = math.sin(t * math.pi) * r.normal(0, amp)
            out.append((x0 + (x1 - x0) * t + px * w, y0 + (y1 - y0) * t + py * w))
    out.append(pts[-1])
    return out


def stain_path(canvas, pts, width, colour, strength=0.55, seed=3):
    m = Image.new('L', (MW, MH), 0)
    d = ImageDraw.Draw(m)
    d.line([(round(x), round(y)) for x, y in pts], fill=255, width=int(width), joint='curve')
    b = np.asarray(m.filter(ImageFilter.GaussianBlur(max(1, width * 0.35))), dtype=np.float64) / 255
    n = comp.noise(MW, MH, (5, 5), seed)
    a = np.clip(b * (0.75 + 0.4 * n) * strength, 0, 0.85)
    base = np.asarray(canvas.convert('RGB'), dtype=np.float64)
    out = base * (1 - a[..., None]) + np.array(colour, dtype=np.float64) * a[..., None]
    canvas.paste(Image.fromarray(np.uint8(np.clip(out, 0, 255))).convert('RGBA'))


def road(canvas, paths, seed=3):
    for i, cells in enumerate(paths):
        pts = _wobbly([centre(*c) for c in cells], 1.4, seed + i)
        stain_path(canvas, pts, 11, (92, 70, 42), 0.62, seed + i)
        stain_path(canvas, pts, 7, (176, 146, 96), 0.5, seed + 20 + i)
    s = 3
    layer = Image.new('RGBA', (MW * s, MH * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for i, cells in enumerate(paths):
        pts = _wobbly([centre(*c) for c in cells], 1.4, seed + i)
        sp = [(x * s, y * s) for x, y in pts]
        d.line(sp, fill=(58, 42, 24, 140), width=int(4.2 * s), joint='curve')
        d.line(sp, fill=(214, 190, 138, 210), width=int(2.4 * s), joint='curve')
    canvas.alpha_composite(layer.resize((MW, MH), Image.LANCZOS))


def railroad(canvas, paths):
    for cells in paths:
        pts = [centre(*c) for c in cells]
        stain_path(canvas, pts, 14, (74, 64, 52), 0.45, 9)
    s = 3
    layer = Image.new('RGBA', (MW * s, MH * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    # ~1/10 of a 128px cell: track gauge ~12px, ties ~13px
    for cells in paths:
        pts = [centre(*c) for c in cells]
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            L = math.hypot(x1 - x0, y1 - y0) or 1
            ux, uy = (x1 - x0) / L, (y1 - y0) / L
            px, py = -uy, ux
            for k in range(int(L // 5.5) + 1):
                cx, cy = x0 + ux * k * 5.5, y0 + uy * k * 5.5
                d.line(((cx - px * 6.2) * s, (cy - py * 6.2) * s, (cx + px * 6.2) * s, (cy + py * 6.2) * s),
                       fill=(86, 60, 36, 255), width=int(1.7 * s))
            for off in (-2.6, 2.6):
                d.line(((x0 + px * off) * s, (y0 + py * off) * s, (x1 + px * off) * s, (y1 + py * off) * s),
                       fill=(28, 28, 32, 255), width=max(s, int(1.15 * s)))
            d.line(((x0 + px * -2.6) * s, (y0 + py * -2.6) * s, (x1 + px * -2.6) * s, (y1 + py * -2.6) * s),
                   fill=(170, 170, 176, 90), width=s)
    canvas.alpha_composite(layer.resize((MW, MH), Image.LANCZOS))


def pollution(canvas, c, r, seed=5):
    m = Image.new('L', (MW, MH), 0)
    ImageDraw.Draw(m).ellipse((c * CELL + 14, r * CELL + 22, (c + 1) * CELL - 12, (r + 1) * CELL - 10), fill=255)
    b = np.asarray(m.filter(ImageFilter.GaussianBlur(11)), dtype=np.float64) / 255
    n = comp.noise(MW, MH, (6, 6), seed) * 0.55 + comp.noise(MW, MH, (2, 2), seed + 1) * 0.45
    a = np.clip((b + (n - 0.5) * 0.65 - 0.28) * 2.0, 0, 0.78)
    hard = np.zeros((MH, MW))
    hard[r * CELL + 2:(r + 1) * CELL - 2, c * CELL + 2:(c + 1) * CELL - 2] = 1
    a *= hard
    base = np.asarray(canvas.convert('RGB'), dtype=np.float64)
    stain = np.array([48, 38, 30], dtype=np.float64)
    sick = np.array([70, 78, 42], dtype=np.float64)
    mix = stain * (1 - n[..., None] * 0.45) + sick * (n[..., None] * 0.45)
    sheen = (n > 0.66) * 0.22
    out = base * (1 - a[..., None]) + mix * a[..., None] + np.array([80, 70, 100]) * (sheen * a)[..., None]
    canvas.paste(Image.fromarray(np.uint8(np.clip(out, 0, 255))).convert('RGBA'))


def ground_patch(canvas, cx, cy, rx, ry, colour, strength=0.5, seed=7):
    m = Image.new('L', (MW, MH), 0)
    ImageDraw.Draw(m).ellipse((cx - rx, cy - ry, cx + rx, cy + ry), fill=255)
    b = np.asarray(m.filter(ImageFilter.GaussianBlur(5)), dtype=np.float64) / 255
    n = comp.noise(MW, MH, (5, 5), seed)
    a = np.clip((b + (n - 0.5) * 0.45 - 0.22) * 2.1, 0, 1) * strength
    base = np.asarray(canvas.convert('RGB'), dtype=np.float64)
    out = base * (1 - a[..., None]) + np.array(colour, dtype=np.float64) * a[..., None]
    canvas.paste(Image.fromarray(np.uint8(np.clip(out, 0, 255))).convert('RGBA'))


def contact_shadow(canvas, cx, cy, rx, ry, alpha=100):
    layer = Image.new('RGBA', (MW, MH), (0, 0, 0, 0))
    ImageDraw.Draw(layer).ellipse((cx - rx, cy - ry, cx + rx, cy + ry), fill=(16, 12, 8, alpha))
    canvas.alpha_composite(layer.filter(ImageFilter.GaussianBlur(3.5)))


def enhance(img, color=1.12, contrast=1.1, sharp=70):
    rgb, al = img.convert('RGB'), img.getchannel('A')
    rgb = ImageEnhance.Color(rgb).enhance(color)
    rgb = ImageEnhance.Contrast(rgb).enhance(contrast)
    rgb = rgb.filter(ImageFilter.UnsharpMask(1.0, sharp, 2))
    out = rgb.convert('RGBA')
    out.putalpha(al)
    return out


def sprite(name, fit_px, colour=None, color=1.12, contrast=1.1, sharp=70):
    key = (name, round(fit_px), colour, color, contrast, sharp)
    if key not in _spr:
        img = keyed(name)
        if colour:
            img = recolour(img, colour)
        k = fit_px / max(img.size)
        img = img.resize((max(1, round(img.width * k)), max(1, round(img.height * k))), Image.LANCZOS)
        _spr[key] = enhance(img, color, contrast, sharp)
    return _spr[key]


def city_sprite(name):
    key = ('cityw', name, CITY_FILL)
    if key not in _spr:
        img = keyed(name)
        k = (CELL * CITY_FILL) / img.width
        if img.height * k > CELL - 8 + CITY_UP:
            k = (CELL - 8 + CITY_UP) / img.height
        img = img.resize((max(1, round(img.width * k)), max(1, round(img.height * k))), Image.LANCZOS)
        _spr[key] = enhance(img, color=1.24, contrast=1.38, sharp=110)
    return _spr[key]


def fade_base(spr, frac=0.16):
    a = np.asarray(spr.getchannel('A'), dtype=np.float64)
    h = a.shape[0]
    ramp = np.ones(h)
    k = max(2, int(h * frac))
    ramp[h - k:] = np.linspace(1, 0.28, k)
    out = spr.copy()
    out.putalpha(Image.fromarray(np.uint8(a * ramp[:, None])))
    return out


def fade_grass(spr):
    a = np.asarray(spr, dtype=np.float64)
    r, g, b, al = a[..., 0], a[..., 1], a[..., 2], a[..., 3]
    yellow = ((r + g) / 2) - b
    grass = np.clip((yellow - 28) / 55, 0, 1) * np.clip((g - b - 8) / 40, 0, 1)
    wood = np.clip((r - g - 12) / 35, 0, 1)
    grey = 1 - np.clip((np.abs(r - g) + np.abs(g - b)) / 40, 0, 1)
    keep = np.clip(1 - grass * (1 - wood) * (1 - grey * 0.5), 0, 1)
    keep = np.maximum(keep, wood)
    a[..., 3] = al * keep
    return Image.fromarray(np.uint8(np.clip(a, 0, 255)), 'RGBA')


def toned(spr, canvas, x, y, amount=0.14, sat=0.92):
    x = max(0, min(x, MW - 1))
    y = max(0, min(y, MH - 1))
    x1, y1 = min(MW, x + spr.width), min(MH, y + spr.height)
    if x1 <= x or y1 <= y:
        return spr
    under = canvas.crop((x, y, x1, y1)).convert('RGB').filter(ImageFilter.GaussianBlur(8))
    rgb = ImageEnhance.Color(spr.convert('RGB')).enhance(sat)
    crop = rgb.crop((0, 0, x1 - x, y1 - y))
    under = under.resize(crop.size)
    blend = Image.blend(crop, under, amount)
    out = spr.copy()
    out.paste(blend, (0, 0))
    out.putalpha(spr.getchannel('A'))
    return out


def outline_clip(canvas, spr, x, y, colour, size, box):
    pad = size + 2
    big = Image.new('L', (spr.width + 2 * pad, spr.height + 2 * pad), 0)
    big.paste(spr.getchannel('A'), (pad, pad))
    ring = big.filter(ImageFilter.MaxFilter(size)).filter(ImageFilter.GaussianBlur(0.5))
    layer = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
    layer.paste(colour + (255,), (x - pad, y - pad), ring)
    mask = Image.new('L', canvas.size, 0)
    ImageDraw.Draw(mask).rectangle(box, fill=255)
    layer.putalpha(ImageChops.multiply(layer.getchannel('A'), mask))
    canvas.alpha_composite(layer)


def pick_adit():
    for n in ('ukiyo-e-M-adit', 'ukiyo-e-M-mine2', 'ukiyo-e-M-mine'):
        if os.path.exists(f'{comp.RAW}/{n}.png'):
            return n
    return 'ukiyo-e-M-mine'


class V2(states.Scene):
    def __init__(self, outline=True):
        super().__init__(outline)
        self._shift = (0, 0)

    def keep(self, c, r, x, y, w, h, what, up=0):
        x0, y0 = c * CELL, r * CELL
        self.boxes.append((c, r, x, y, x + w, y + h, what, up))

    def check(self):
        for c, r, x0, y0, x1, y1, what, *rest in self.boxes:
            up = rest[0] if rest else 0
            left, top, right, bot = c * CELL, r * CELL, (c + 1) * CELL, (r + 1) * CELL
            if x0 < left - 1 or x1 > right + 1 or y1 > bot + 1 or y0 < top - up - 1:
                raise AssertionError((what, c, r, x0, y0, x1, y1, 'cell', left, top, right, bot, 'up', up))

    def place(self, spr, c, r, dx=0, dy=0, outline=None, shadow=True, what='', up=0):
        sx, sy = self._shift
        cx, cy = centre(c, r)
        x, y = round(cx - spr.width / 2 + dx + sx), round(cy - spr.height / 2 + dy + sy)
        if shadow:
            shadow_layer(self.canvas, spr, x, y)
        if outline if outline is not None else self.outline:
            outline_layer(self.canvas, spr, x, y, colour=CREAM, size=3)
        self.canvas.alpha_composite(spr, (x, y))
        self.keep(c, r, x, y, spr.width, spr.height, what or 'spr', up)
        return x, y

    def grounded(self, name, c, r, fit=OBJ_FIT, patch=(158, 132, 82), what='', dy=10, fade=0.22, grass=False):
        full = name if name.startswith('ukiyo-e-') else f'ukiyo-e-M-{name}'
        spr = sprite(full, CELL * fit, color=1.04, contrast=1.06, sharp=50)
        if grass:
            spr = fade_grass(spr)
        cx, cy = centre(c, r)
        x, y = round(cx - spr.width / 2), round(cy - spr.height / 2 + dy)
        base_y = y + spr.height - 2
        # worn earth under the object, then a tight contact shadow so it sits on the print
        ground_patch(self.canvas, cx, base_y - 4, spr.width * 0.52, max(8, spr.height * 0.18), patch, 0.62, seed=c * 17 + r)
        contact_shadow(self.canvas, cx + 1, base_y - 1, spr.width * 0.40, max(4, spr.height * 0.09), 160)
        spr = fade_base(toned(spr, self.canvas, x, y, 0.16, 0.9), fade)
        self.canvas.alpha_composite(spr, (x, y))
        self.keep(c, r, x, y, spr.width, spr.height, what or name)
        return x, y

    def hut(self, c, r):
        name = 'ukiyo-e-M-hut2' if os.path.exists(f'{comp.RAW}/ukiyo-e-M-hut2.png') else 'ukiyo-e-M-hut'
        self.grounded(name, c, r, fit=0.46, patch=(168, 140, 92), what='hut', dy=8, fade=0.2)

    def horses(self, c, r):
        self.grounded('horses', c, r, fit=0.40, patch=(150, 138, 78), what='horses', dy=12, fade=0.12)

    def fish(self, c, r):
        spr = sprite('ukiyo-e-M-fish', CELL * 0.40, color=1.08, contrast=1.08, sharp=55)
        cx, cy = centre(c, r)
        x, y = round(cx - spr.width / 2), round(cy - spr.height / 2 + 6)
        self.ripples(cx, y + spr.height - 10, spr.width * 0.42)
        self.canvas.alpha_composite(fade_base(spr, 0.22), (x, y))
        self.keep(c, r, x, y, spr.width, spr.height, 'fish')

    def mine(self, c, r):
        """Print a hillside mine cut into this cell. Forest at the crop corners blends into the map."""
        x0, y0 = c * CELL, r * CELL
        dest = np.asarray(self.canvas.crop((x0, y0, x0 + CELL, y0 + CELL)).convert('RGB'), dtype=np.float64)
        src_path, crop = None, (0.34, 0.26, 0.66, 0.78)
        for nme, box in (
            ('ukiyo-e-T-mineforest', (0.34, 0.26, 0.66, 0.78)),
            ('ukiyo-e-T-mineforest2', (0.36, 0.22, 0.74, 0.86)),
            ('ukiyo-e-T-minehill', (0.32, 0.28, 0.68, 0.76)),
        ):
            p = f'{comp.RAW}/{nme}.png'
            if os.path.exists(p):
                src_path, crop = p, box
                break
        n = comp.noise(CELL, CELL, (3, 3), 21 + c * 3 + r)
        if src_path:
            src = Image.open(src_path).convert('RGB')
            sw, sh = src.size
            x1, y1, x2, y2 = crop
            cut = src.crop((int(sw * x1), int(sh * y1), int(sw * x2), int(sh * y2))).resize((CELL, CELL), Image.LANCZOS)
            cut = ImageEnhance.Color(cut).enhance(1.04)
            cut = ImageEnhance.Contrast(cut).enhance(1.28)
            cut = ImageEnhance.Brightness(cut).enhance(1.06)
            arr = np.asarray(cut, dtype=np.float64)
        else:
            arr = np.array([140, 118, 84], dtype=np.float64) * (0.75 + 0.35 * n[..., None])
        mask = Image.new('L', (CELL, CELL), 0)
        ImageDraw.Draw(mask).rounded_rectangle((6, 8, CELL - 7, CELL - 6), 18, fill=255)
        a = np.asarray(mask.filter(ImageFilter.GaussianBlur(3.5)), dtype=np.float64) / 255
        a = np.clip(a + (n - 0.5) * 0.06, 0, 1)
        a[:2, :] = 0
        a[-2:, :] = 0
        a[:, :2] = 0
        a[:, -2:] = 0
        out = dest * (1 - a[..., None] * 0.94) + arr * (a[..., None] * 0.94)
        ring = np.exp(-((a - 0.55) / 0.12) ** 2) * 0.18
        out = out * (1 - ring[..., None]) + np.array(INK, dtype=np.float64) * ring[..., None]
        grain = np.asarray(GRAIN.crop((x0, y0, x0 + CELL, y0 + CELL)), dtype=np.float64) / 255
        out = out * (0.92 + 0.08 * grain)
        patch = Image.fromarray(np.uint8(np.clip(out, 0, 255))).convert('RGBA')
        self.canvas.paste(patch, (x0, y0))
        self.keep(c, r, x0 + 4, y0 + 4, CELL - 8, CELL - 8, 'mine')

    def ripples(self, cx, cy, rx, wake=0):
        s = 3
        w = int(rx * 2 + 56 + max(wake, 0) + 10)
        h = 52
        layer = Image.new('RGBA', (max(8, w) * s, h * s), (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        ox, oy = 24 + rx, 24
        for k, (rr, wdt) in enumerate(((0.92, 2.2), (1.18, 1.6), (1.42, 1.1))):
            box = ((ox - rx * rr) * s, (oy - rx * rr * 0.18) * s, (ox + rx * rr) * s, (oy + rx * rr * 0.18) * s)
            d.arc(box, 200, 340, fill=INK + (180,), width=int((wdt + 1.1) * s))
            d.arc(box, 200, 340, fill=(248, 244, 230, 230), width=int(wdt * s))
        if wake:
            cream = (248, 244, 230, 230)
            for off, spread, wd in ((-3.2, 1.15, 2.4), (3.2, 1.15, 2.4)):
                pts = [((ox + rx * 0.4 + t) * s, (oy + off * (1 + t / max(wake, 1) * spread)) * s) for t in np.linspace(0, wake * 0.85, 6)]
                d.line(pts, fill=INK + (170,), width=int((wd + 1.0) * s))
                d.line(pts, fill=cream, width=int(wd * s))
        img = layer.resize((max(1, layer.width // s), max(1, layer.height // s)), Image.LANCZOS)
        px, py = round(cx - ox), round(cy - oy)
        self.canvas.alpha_composite(img, (px, py))

    def unit(self, kind, c, r, owner='p', state=(), stack=1, stack_style='badge', cargo=0, select=None, outline=None, **kw):
        if kind in SHIPS:
            return self.ship(kind, c, r, owner, cargo=cargo, select=select, outline=outline, **kw)
        if kind in AIR:
            return self.aircraft(kind, c, r, owner, select=select, outline=outline, **kw)
        col = BLUE if owner == 'p' else RED
        uname, _ = UNITS[kind]
        spr = sprite(uname, CELL * UNIT_FIT, col, color=1.08, contrast=1.08, sharp=55)
        if 'sentry' in state:
            spr = faded(spr)
        if 'nomoves' in state:
            spr = dimmed(spr)
        dx = dy = 0
        copies = 0
        if stack > 1 and stack_style in ('offset', 'both'):
            copies = 1 if stack_style == 'both' else min(stack - 1, 2)
            dx = dy = -copies * 6 / 2
        ring = None
        if 'fortified' in state:
            ring = sprite('ukiyo-e-M-fort', CELL * 0.82, color=1.0, contrast=1.05, sharp=40)
            fx, fy = self.place(ring, c, r, dy=12, outline=False, shadow=False, what='fort')
            dy -= 6
        cx, cy = centre(c, r)
        x, y = round(cx - spr.width / 2 + dx), round(cy - spr.height / 2 + dy)
        for k in range(copies, 0, -1):
            back = ImageEnhance.Brightness(spr.convert('RGB')).enhance(0.78).convert('RGBA')
            back.putalpha(spr.getchannel('A'))
            self.place(back, c, r, dx + k * 6, dy + k * 6, outline=outline, what='copy')
        x, y = self.place(spr, c, r, dx, dy, outline=outline, shadow=copies == 0, what=kind)
        if ring is not None:
            front = ring.crop((0, int(ring.height * 0.55), ring.width, ring.height))
            self.canvas.alpha_composite(front, (fx, fy + int(ring.height * 0.55)))
        if kind in FLAGGED:
            self.mark(sashimono(col), c, r, x - 6, y - 14, 'flag')
        if 'veteran' in state:
            self.mark(star(22), c, r, x + spr.width - 4, y - 4, 'veteran')
        n = cargo or (stack if stack > 1 and stack_style in ('badge', 'both') else 0)
        if n:
            self.mark(count_seal(n), c, r, x + spr.width - 16, y + spr.height - 18, 'count')
        if select == 'yellow':
            yellow_square(ImageDraw.Draw(self.canvas), c, r)
        elif select == 'brush':
            self.canvas.alpha_composite(brush_frame(c, r), (c * CELL, r * CELL))
        return x, y

    def ship(self, kind, c, r, owner='p', cargo=0, select=None, outline=None, **kw):
        col = BLUE if owner == 'p' else RED
        uname, _ = UNITS[kind]
        spr = sprite(uname, CELL * SHIP_FIT, col, color=1.08, contrast=1.1, sharp=60)
        cx, cy = centre(c, r)
        x = round(cx - spr.width / 2)
        y = round(cy - spr.height / 2 + 2)
        water_y = y + int(spr.height * 0.78)
        ref = spr.transpose(Image.Transpose.FLIP_TOP_BOTTOM).resize((spr.width, max(6, int(spr.height * 0.28))))
        ref_rgb = ImageEnhance.Brightness(ImageEnhance.Color(ref.convert('RGB')).enhance(0.4)).enhance(0.42)
        ra = np.asarray(ref.getchannel('A'), dtype=np.float64) * 0.52
        rows = np.arange(ra.shape[0])
        ra *= (np.sin(rows * 1.35) > -0.35)[:, None] * np.linspace(1, 0.2, ra.shape[0])[:, None]
        ref_img = ref_rgb.convert('RGBA')
        ref_img.putalpha(Image.fromarray(np.uint8(np.clip(ra, 0, 255))))
        self.canvas.alpha_composite(ref_img, (x, round(water_y - 1)))
        self.ripples(cx, water_y + 3, spr.width * 0.4, wake=22)
        if outline if outline is not None else self.outline:
            box = (c * CELL + 1, r * CELL + 1, (c + 1) * CELL - 2, (r + 1) * CELL - 2)
            outline_clip(self.canvas, spr, x, y, CREAM, 3, box)
        self.canvas.alpha_composite(spr, (x, y))
        self.keep(c, r, x, y, spr.width, spr.height, kind)
        if cargo:
            self.mark(count_seal(cargo), c, r, x + spr.width - 16, y + spr.height - 18, 'count')
        if kind in FLAGGED:
            self.mark(sashimono(col), c, r, x - 4, y - 10, 'flag')
        if select == 'yellow':
            yellow_square(ImageDraw.Draw(self.canvas), c, r)
        return x, y

    def aircraft(self, kind, c, r, owner='p', select=None, outline=None, **kw):
        col = BLUE if owner == 'p' else RED
        uname, _ = UNITS[kind]
        spr = sprite(uname, CELL * AIR_FIT, col, color=1.1, contrast=1.12, sharp=70)
        cx, cy = centre(c, r)
        x, y = round(cx - spr.width / 2 - 10), round(cy - spr.height / 2 - 16)
        sw, shh = int(spr.width * 0.72), max(6, int(spr.height * 0.38))
        sh = spr.getchannel('A').resize((sw, shh)).filter(ImageFilter.GaussianBlur(2.8)).point(lambda v: int(v * 0.55))
        sx, sy = x + 28, y + spr.height - 6
        self.canvas.paste((18, 16, 12, 255), (sx, sy), sh)
        if outline if outline is not None else self.outline:
            box = (c * CELL + 1, r * CELL + 1, (c + 1) * CELL - 2, (r + 1) * CELL - 2)
            outline_clip(self.canvas, spr, x, y, CREAM, 3, box)
        self.canvas.alpha_composite(spr, (x, y))
        self.keep(c, r, x, y, spr.width, spr.height, kind, up=8)
        if select == 'yellow':
            yellow_square(ImageDraw.Draw(self.canvas), c, r)
        return x, y

    def city_plate(self, c, r, col):
        """Trodden paved footprint painted into the ground, owner colour as a wall-band, not a token card."""
        x0, y0 = c * CELL, r * CELL
        dest = np.asarray(self.canvas.crop((x0, y0, x0 + CELL, y0 + CELL)).convert('RGB'), dtype=np.float64)
        mask = Image.new('L', (CELL, CELL), 0)
        ImageDraw.Draw(mask).rounded_rectangle((3, 8, CELL - 4, CELL - 4), 12, fill=255)
        a = np.asarray(mask.filter(ImageFilter.GaussianBlur(1.8)), dtype=np.float64) / 255
        n = comp.noise(CELL, CELL, (4, 4), 40 + c * 5 + r) * 0.55 + comp.noise(CELL, CELL, (2, 2), 41 + c) * 0.45
        a = np.clip(a + (n - 0.5) * 0.08, 0, 1)
        hard = np.zeros((CELL, CELL))
        hard[2:-2, 2:-2] = 1
        a *= hard
        earth = np.array([86, 66, 42], dtype=np.float64)
        pave = np.array([168, 142, 96], dtype=np.float64)
        cream = np.array(CREAM, dtype=np.float64)
        cob = pave * (0.70 + 0.30 * n[..., None]) + earth * (1 - n[..., None]) * 0.35
        cob = cob * 0.88 + cream * 0.12
        fleck = comp.noise(CELL, CELL, (1, 1), 50 + c)
        cob = cob * (0.90 + 0.14 * fleck[..., None])
        grain = np.asarray(GRAIN.crop((x0, y0, x0 + CELL, y0 + CELL)), dtype=np.float64) / 255
        cob = cob * (0.80 + 0.20 * grain)
        out = dest * (1 - a[..., None] * 0.96) + cob * (a[..., None] * 0.96)
        # owner colour as a wall-band around the footprint (a ring, not a filled card)
        ring_owner = np.clip((a - 0.55) * 10, 0, 1) * np.clip((0.92 - a) * 9, 0, 1)
        ring_ink = np.clip((a - 0.38) * 10, 0, 1) * np.clip((0.58 - a) * 10, 0, 1)
        ring_cream = np.clip((a - 0.50) * 12, 0, 1) * np.clip((0.68 - a) * 12, 0, 1)
        out = out * (1 - ring_ink[..., None] * 0.90) + np.array(INK, dtype=np.float64) * ring_ink[..., None] * 0.90
        out = out * (1 - ring_cream[..., None] * 0.70) + cream * ring_cream[..., None] * 0.70
        out = out * (1 - ring_owner[..., None] * 0.95) + np.array(col, dtype=np.float64) * ring_owner[..., None] * 0.95
        patch = Image.fromarray(np.uint8(np.clip(out, 0, 255))).convert('RGBA')
        self.canvas.paste(patch, (x0, y0))

    def city(self, kind, c, r, owner='p', size=3, capital=False, garrison=0, disorder=False, plate=True, **kw):
        col = BLUE if owner == 'p' else RED
        cname, _ = CITIES[kind]
        x0, y0 = c * CELL, r * CELL
        if plate:
            self.city_plate(c, r, col)
        spr = city_sprite(cname)
        x = round(x0 + (CELL - spr.width) / 2)
        y = y0 + CELL - 5 - spr.height
        shadow_layer(self.canvas, spr, x, y, 0.78)
        box = (x0 + 1, y0 - CITY_UP + 1, x0 + CELL - 2, y0 + CELL - 2)
        outline_clip(self.canvas, spr, x, y, INK, 11, box)
        outline_clip(self.canvas, spr, x, y, CREAM, 7, box)
        outline_clip(self.canvas, spr, x, y, INK, 3, box)
        self.canvas.alpha_composite(spr, (x, y))
        self.keep(c, r, x, max(y, y0), spr.width, spr.height - max(0, y0 - y), kind, up=CITY_UP)
        if disorder:
            sm = sprite('ukiyo-e-M-smoke', CELL * 0.55, color=1.0, contrast=1.1, sharp=40)
            sm.putalpha(sm.getchannel('A').point(lambda v: int(v * 0.75)))
            self.canvas.alpha_composite(sm, (x0 + CELL - sm.width - 4, y0 + 2))
        b = city_badge(size, col)
        self.mark(b, c, r, x0 + 3, y0 + 3, 'badge')
        if capital:
            self.mark(capital_mon(26), c, r, x0 + 3 + b.width + 2, y0 + 4, 'capital')
        if garrison:
            self.mark(count_seal(garrison), c, r, x0 + CELL - 30, y0 + CELL - 30, 'garrison')

    def smoke_wisp(self, c, r):
        sm = sprite('ukiyo-e-M-smoke', CELL * 0.36, color=0.85, contrast=1.05, sharp=30)
        a = np.array(sm, dtype=np.uint8, copy=True)
        h = a.shape[0]
        fade = (np.linspace(0.2, 0.75, h)[:, None] * (a[..., 3] / 255.0))
        fade[int(h * 0.62):] *= 0.08
        a[..., 3] = np.clip(fade * 255, 0, 255).astype(np.uint8)
        sm = Image.fromarray(a, 'RGBA')
        cx = c * CELL + CELL // 2 - sm.width // 2 + 8
        cy = r * CELL + 4
        self.canvas.alpha_composite(sm, (cx, cy))

    def screen(self, sel_kind='rider', sel_cell=(4, 5), grid=False, portrait='ukiyo-e-portrait'):
        self.check()
        m = comp2.with_grid(self.canvas) if grid else self.canvas
        portrait_im = Image.open(f'{comp.RAW}/{portrait}.png').convert('RGB')
        us = recolour(keyed(UNITS[sel_kind][0]), BLUE)
        c, r = sel_cell
        side = comp.sidebar('ukiyo-e', states.PLATE, states.VIEW, portrait_im, us,
                            states.GROUND.crop((c * CELL, r * CELL, (c + 1) * CELL, (r + 1) * CELL)))
        full = Image.new('RGB', (comp.W, comp.H))
        full.paste(side, (0, 0))
        full.paste(m.convert('RGB'), (comp.SIDE, 0))
        return full


# ---------- scenes ----------
FOG = [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (0, 2), (0, 3), (9, 6), (9, 7), (8, 7)]


def paddy_name():
    return _paddy_tex_name()


def roman_ancient():
    s = V2(True)
    farmland(s.canvas, [(4, 2), (5, 2), (4, 3)], kind='ukiyo-e-T-fields', tile=920, seed=2)
    road(s.canvas, [[(5, 3), (4, 4), (4, 5), (3, 6)], [(5, 3), (6, 2)]])
    s.horses(4, 7)
    s.fish(8, 4)
    s.hut(1, 3)
    s.mine(0, 5)
    fog(s.canvas, FOG)
    goto_path(s.canvas, [(4, 5), (3, 4), (2, 3), (2, 2)])
    s.city('rcity', 5, 3, 'p', size=8, capital=True, garrison=2)
    s.city('rtown', 3, 6, 'p', size=4, disorder=True)
    s.city('rtown', 7, 1, 'r', size=3)
    s.unit('legion', 6, 2, 'p', state=('fortified', 'veteran'))
    s.unit('legion', 7, 0, 'r')
    s.unit('cavalry', 8, 2, 'r')
    s.unit('legion', 4, 6, 'p', stack=3, stack_style='badge')
    s.unit('rsettler', 5, 2, 'p', state=('nomoves',))
    s.unit('trireme', 7, 5, 'p', cargo=2)
    s.unit('cavalry', 4, 5, 'p', select='yellow')
    return s


def modern():
    s = V2(True)
    farmland(s.canvas, [(4, 7), (5, 7), (3, 7)], kind='ukiyo-e-T-fields', tile=920, seed=4)
    road(s.canvas, [[(3, 6), (4, 5), (4, 4), (5, 3), (6, 2)]])
    railroad(s.canvas, [[(3, 6), (4, 5), (4, 4), (5, 3), (6, 2)]])
    pollution(s.canvas, 4, 3)
    s.smoke_wisp(4, 3)
    pollution(s.canvas, 5, 2, seed=8)
    s.smoke_wisp(5, 2)
    s.fish(8, 5)
    goto_path(s.canvas, [(4, 5), (3, 4), (3, 3), (4, 2), (5, 1)])
    s.city('wcity', 5, 3, 'p', size=14, capital=True, garrison=3)
    s.city('wcity2', 3, 6, 'p', size=11)
    s.city('acity', 7, 1, 'r', size=9)
    s.unit('artillery', 6, 2, 'p', state=('fortified', 'veteran'))
    s.unit('mech', 6, 1, 'p', stack=3, stack_style='badge')
    s.unit('battleship', 8, 3, 'p')
    s.unit('transport', 7, 5, 'p', cargo=3)
    s.unit('atank', 7, 0, 'r')
    s.unit('meiji', 8, 2, 'r')
    s.unit('bomber', 7, 2, 'p')
    s.unit('armor', 4, 5, 'p', select='yellow')
    return s


def asian_medieval():
    s = V2(True)
    farmland(s.canvas, [(4, 2), (5, 2), (4, 3)], kind=paddy_name(), tile=340, seed=1)
    road(s.canvas, [[(5, 3), (4, 4), (4, 5), (3, 6)], [(5, 3), (6, 2)]])
    s.horses(4, 7)
    s.fish(8, 4)
    s.hut(1, 3)
    s.mine(0, 5)
    fog(s.canvas, FOG)
    goto_path(s.canvas, [(4, 5), (3, 4), (2, 3), (2, 2)])
    s.city('castle', 5, 3, 'p', size=8, capital=True, garrison=2)
    s.city('town', 3, 6, 'p', size=4, disorder=True)
    s.city('han', 7, 1, 'r', size=3)
    s.unit('foot', 6, 2, 'p', state=('fortified', 'veteran'))
    s.unit('foot', 7, 0, 'r')
    s.unit('rider', 8, 2, 'r')
    s.unit('foot', 4, 6, 'p', stack=3, stack_style='badge')
    s.unit('settler', 5, 2, 'p', state=('nomoves',))
    s.unit('ship', 7, 5, 'p', cargo=2)
    s.unit('rider', 4, 5, 'p', select='yellow')
    return s


def save_scene(s, name, sel_kind, sel_cell, portrait):
    s.screen(sel_kind, sel_cell, portrait=portrait).save(f'{OUT}/{name}.jpg', quality=93)
    s.screen(sel_kind, sel_cell, grid=True, portrait=portrait).save(f'{OUT}/{name}-grid.jpg', quality=93)
    print('saved', name, flush=True)


def label(d, xy, text, size=22):
    font = ImageFont.truetype(SANS, size)
    d.text(xy, text, fill=INK, font=font)


def _pad_to(im, w, h):
    out = Image.new('RGB', (w, h), PAPER)
    im = im.convert('RGB')
    out.paste(im, ((w - im.width) // 2, (h - im.height) // 2))
    return out


def sheet_pair(rows, path, pad=18, lab=34, caption_h=48, col_heads=('Last round', 'After')):
    """rows: list of (label, before_img, after_img) already cropped."""
    mw = max(max(a.width, b.width) for _, a, b in rows)
    mh = max(max(a.height, b.height) for _, a, b in rows)
    rows = [(name, _pad_to(a, mw, mh), _pad_to(b, mw, mh)) for name, a, b in rows]
    pw, ph = mw, mh
    img = Image.new('RGB', (pad * 3 + pw * 2, caption_h + len(rows) * (ph + lab + pad) + pad), PAPER)
    d = ImageDraw.Draw(img)
    font = ImageFont.truetype(SANS, 28)
    d.text((pad, 12), col_heads[0], fill=INK, font=font)
    d.text((pad * 2 + pw, 12), col_heads[1], fill=INK, font=font)
    lf = ImageFont.truetype(SANS, 22)
    for i, (name, a, b) in enumerate(rows):
        y = caption_h + i * (ph + lab + pad)
        d.text((pad, y + 4), name, fill=INK, font=lf)
        img.paste(a, (pad, y + lab))
        img.paste(b, (pad * 2 + pw, y + lab))
        d.rectangle((pad - 1, y + lab - 1, pad + pw, y + lab + ph), outline=INK, width=1)
        d.rectangle((pad * 2 + pw - 1, y + lab - 1, pad * 2 + pw * 2, y + lab + ph), outline=INK, width=1)
    img.save(path, quality=93)
    print('saved', path, flush=True)
    return img


def crop_cells(canvas, cells, m=8, grid=True):
    cs = [c for c, r in cells]
    rs = [r for c, r in cells]
    c0, c1, r0, r1 = min(cs), max(cs) + 1, min(rs), max(rs) + 1
    src = comp2.with_grid(canvas) if grid else canvas
    return src.convert('RGB').crop((c0 * CELL - m, r0 * CELL - m, c1 * CELL + m, r1 * CELL + m))


def improvements_sheet():
    """Before = last-round states.Scene treatment; after = V2 painted-in treatment."""
    specs = [
        ('Irrigation, one cell', [(5, 2)], 'irr1'),
        ('Irrigation, three joined', [(4, 2), (5, 2), (6, 2)], 'irr3'),
        ('Mine in a hillside', [(0, 5)], 'mine'),
        ('Road', [(4, 4), (4, 5)], 'road'),
        ('Railroad', [(4, 4), (4, 5)], 'rail'),
        ('Road and railroad', [(4, 4), (4, 5)], 'both'),
        ('Pollution', [(4, 3)], 'poll'),
        ('Hut', [(1, 3)], 'hut'),
        ('Resource (horses)', [(4, 7)], 'horses'),
        ('Ship, wake and reflection', [(7, 5)], 'ship'),
        ('Aircraft, ground shadow', [(7, 2)], 'air'),
    ]

    def before_of(kind):
        s = states.Scene(True)
        if kind in ('irr1',):
            states.irrigation(s.canvas, [(5, 2)])
        elif kind == 'irr3':
            states.irrigation(s.canvas, [(4, 2), (5, 2), (6, 2)])
        elif kind == 'mine':
            s.obj('mine', 0, 5, fit=0.52)
        elif kind == 'road':
            states.roads(s.canvas, [[(5, 3), (4, 4), (4, 5), (3, 6)]])
        elif kind == 'rail':
            states.roads(s.canvas, [[(5, 3), (4, 4), (4, 5), (3, 6)]], rail=True)
        elif kind == 'both':
            states.roads(s.canvas, [[(5, 3), (4, 4), (4, 5), (3, 6)]])
            states.roads(s.canvas, [[(5, 3), (4, 4), (4, 5), (3, 6)]], rail=True)
        elif kind == 'poll':
            s.obj('ukiyo-e-W-pollution', 4, 3, fit=0.62)
        elif kind == 'hut':
            s.obj('hut', 1, 3, fit=0.5)
        elif kind == 'horses':
            s.obj('horses', 4, 7, fit=0.5)
        elif kind == 'ship':
            s.unit('trireme', 7, 5, 'p')
        elif kind == 'air':
            s.unit('bomber', 7, 2, 'p')
        return s

    def after_of(kind):
        s = V2(True)
        if kind == 'irr1':
            farmland(s.canvas, [(5, 2)], kind=paddy_name(), tile=340, seed=1)
        elif kind == 'irr3':
            farmland(s.canvas, [(4, 2), (5, 2), (6, 2)], kind=paddy_name(), tile=340, seed=1)
        elif kind == 'mine':
            s.mine(0, 5)
        elif kind == 'road':
            road(s.canvas, [[(5, 3), (4, 4), (4, 5), (3, 6)]])
        elif kind == 'rail':
            railroad(s.canvas, [[(5, 3), (4, 4), (4, 5), (3, 6)]])
        elif kind == 'both':
            road(s.canvas, [[(5, 3), (4, 4), (4, 5), (3, 6)]])
            railroad(s.canvas, [[(5, 3), (4, 4), (4, 5), (3, 6)]])
        elif kind == 'poll':
            pollution(s.canvas, 4, 3)
            s.smoke_wisp(4, 3)
        elif kind == 'hut':
            s.hut(1, 3)
        elif kind == 'horses':
            s.horses(4, 7)
        elif kind == 'ship':
            s.unit('trireme', 7, 5, 'p')
        elif kind == 'air':
            s.unit('bomber', 7, 2, 'p')
        return s

    rows = []
    for name, cells, kind in specs:
        b, a = before_of(kind), after_of(kind)
        # 3-cell irrigation needs a wide crop; others a 2x2-ish neighbourhood
        if kind == 'irr3':
            crop_cells_ = cells
        elif kind in ('road', 'rail', 'both'):
            crop_cells_ = [(4, 4), (4, 5), (5, 4), (5, 5)]
        elif kind == 'air':
            crop_cells_ = [(6, 1), (7, 1), (6, 2), (7, 2)]
        else:
            c, r = cells[0]
            crop_cells_ = [(c, r)]
        bb = crop_cells(b.canvas, crop_cells_, m=10, grid=True)
        aa = crop_cells(a.canvas, crop_cells_, m=10, grid=True)
        k = 2.05 if bb.width < 420 else 1.6
        bb = bb.resize((round(bb.width * k), round(bb.height * k)), Image.LANCZOS)
        aa = aa.resize((round(aa.width * k), round(aa.height * k)), Image.LANCZOS)
        rows.append((name, bb, aa))
        # also dump review crops
        aa.save(f'{WORK}/after-{kind}.png')
        bb.save(f'{WORK}/before-{kind}.png')
    sheet_pair(rows, f'{OUT}/improvements-before-after.jpg')


def cities_sheet():
    """Several faction/era cities, before vs after, unit outline on."""
    cases = [
        ('Roman city + legion, plains', 'rcity', 'legion', (5, 3), (6, 3), 8),
        ('Roman town + cavalry, coast', 'rtown', 'cavalry', (7, 1), (8, 1), 4),
        ('Japanese castle + rider, forest edge', 'castle', 'rider', (2, 5), (3, 5), 8),
        ('Han city + spearman, plains', 'han', 'qin', (4, 6), (5, 6), 6),
        ('Gothic city + knight, plains', 'gothic', 'knight', (5, 3), (6, 3), 7),
        ('Meiji Tokyo + rifle, plains', 'tokyo', 'meiji', (5, 3), (6, 3), 11),
        ('Western industrial + armor', 'wcity', 'armor', (5, 3), (6, 3), 14),
        ('Modern Asian + tank, coast', 'acity', 'atank', (7, 1), (8, 1), 9),
    ]
    rows = []
    for name, city, unit, cc, uc, size in cases:
        sb = states.Scene(True)
        sb.city(city, cc[0], cc[1], 'p', size=size, capital=True)
        sb.unit(unit, uc[0], uc[1], 'p')
        sa = V2(True)
        sa.city(city, cc[0], cc[1], 'p', size=size, capital=True)
        sa.unit(unit, uc[0], uc[1], 'p')
        cells = [cc, uc]
        # include a sliver of the cell above so CITY_UP is visible
        bb = crop_cells(sb.canvas, cells, m=18, grid=True)
        aa = crop_cells(sa.canvas, cells, m=18, grid=True)
        k = 1.85
        bb = bb.resize((round(bb.width * k), round(bb.height * k)), Image.LANCZOS)
        aa = aa.resize((round(aa.width * k), round(aa.height * k)), Image.LANCZOS)
        rows.append((name, bb, aa))
        aa.save(f'{WORK}/city-after-{city}.png')
        bb.save(f'{WORK}/city-before-{city}.png')
    sheet_pair(rows, f'{OUT}/cities-before-after.jpg', col_heads=('Last round (unit outline on)', 'After (unit outline on)'))


def review_crops(s, tag):
    """Zoom crops used for visual QA."""
    g = comp2.with_grid(s.canvas).convert('RGB')
    spots = {
        'city-plains': (5, 3),
        'city-town': (3, 6),
        'city-coast': (7, 1),
        'unit': (4, 5),
        'farm': (5, 2),
        'mine': (0, 5),
        'hut': (1, 3),
        'ship': (7, 5),
        'horses': (4, 7),
    }
    for name, (c, r) in spots.items():
        g.crop((c * CELL - 12, r * CELL - 24, (c + 1) * CELL + 12, (r + 1) * CELL + 12)).save(f'{WORK}/{tag}-{name}.png')


def build_all():
    print('improvements sheet', flush=True)
    improvements_sheet()
    print('cities sheet', flush=True)
    cities_sheet()
    print('roman-ancient', flush=True)
    s = roman_ancient()
    save_scene(s, 'roman-ancient', 'cavalry', (4, 5), 'ukiyo-e-R-portrait')
    review_crops(s, 'ra')
    print('modern', flush=True)
    s = modern()
    save_scene(s, 'modern', 'armor', (4, 5), 'ukiyo-e-W-portrait')
    review_crops(s, 'mo')
    print('asian-medieval', flush=True)
    s = asian_medieval()
    save_scene(s, 'asian-medieval', 'rider', (4, 5), 'ukiyo-e-portrait')
    review_crops(s, 'as')
    print('done', flush=True)


def test_farm():
    """Quick farmland / mine / city occupancy test."""
    s = V2(True)
    farmland(s.canvas, [(5, 2)], kind=paddy_name(), seed=1)
    farmland(s.canvas, [(3, 4), (4, 4), (5, 4)], kind=paddy_name(), seed=3)
    farmland(s.canvas, [(6, 6)], kind='ukiyo-e-T-fields', seed=5)
    farmland(s.canvas, [(7, 6), (8, 6), (7, 7)], kind='ukiyo-e-T-fields', seed=6)
    s.mine(0, 5)
    s.hut(1, 3)
    s.horses(4, 7)
    s.city('rcity', 5, 3, 'p', size=8, capital=True)
    s.unit('legion', 6, 3, 'p')
    s.city('castle', 3, 6, 'p', size=6)
    s.unit('rider', 4, 6, 'p')
    s.city('wcity', 7, 1, 'p', size=12)
    s.unit('armor', 8, 1, 'p')
    s.city('han', 2, 5, 'p', size=7)
    s.unit('qin', 3, 5, 'p')
    road(s.canvas, [[(5, 3), (4, 5)]])
    railroad(s.canvas, [[(3, 6), (4, 5)]])
    s.check()
    g = comp2.with_grid(s.canvas).convert('RGB')
    g.save(f'{WORK}/test-map.png')
    for name, box in {
        'paddy1': (5 * CELL - 8, 2 * CELL - 8, 6 * CELL + 8, 3 * CELL + 8),
        'paddy3': (3 * CELL - 8, 4 * CELL - 8, 6 * CELL + 8, 5 * CELL + 8),
        'fields': (6 * CELL - 8, 6 * CELL - 8, 9 * CELL + 8, 8 * CELL + 8),
        'mine': (0, 5 * CELL - 16, CELL + 8, 6 * CELL + 8),
        'city-roman': (5 * CELL - 16, 2 * CELL - 8, 7 * CELL + 16, 4 * CELL + 8),
        'city-castle': (3 * CELL - 16, 5 * CELL - 8, 5 * CELL + 16, 7 * CELL + 8),
        'city-west': (7 * CELL - 16, 0 * CELL - 8, 9 * CELL + 16, 2 * CELL + 8),
        'city-forest': (2 * CELL - 16, 4 * CELL - 8, 4 * CELL + 16, 6 * CELL + 8),
        'rail': (3 * CELL - 8, 5 * CELL - 8, 5 * CELL + 8, 7 * CELL + 8),
        'hut': (1 * CELL - 8, 3 * CELL - 8, 2 * CELL + 8, 4 * CELL + 8),
        'horses': (4 * CELL - 8, 7 * CELL - 8, 5 * CELL + 8, 8 * CELL + 8),
    }.items():
        g.crop(box).save(f'{WORK}/test-{name}.png')
    # procedural paddies as an extra comparison crop
    sp = V2(True)
    paddy_field(sp.canvas, [(4, 2), (5, 2), (6, 2)], seed=2)
    paddy_field(sp.canvas, [(5, 4)], seed=4)
    gp = comp2.with_grid(sp.canvas).convert('RGB')
    gp.crop((4 * CELL - 8, 2 * CELL - 8, 7 * CELL + 8, 3 * CELL + 8)).save(f'{WORK}/test-paddy-proc3.png')
    gp.crop((5 * CELL - 8, 4 * CELL - 8, 6 * CELL + 8, 5 * CELL + 8)).save(f'{WORK}/test-paddy-proc1.png')
    print('test crops in', WORK, flush=True)


if __name__ == '__main__':
    cmd = sys.argv[1:] or ['all']
    if 'test' in cmd:
        test_farm()
    if 'improvements' in cmd:
        improvements_sheet()
    if 'cities' in cmd:
        cities_sheet()
    if 'scenes' in cmd:
        save_scene(roman_ancient(), 'roman-ancient', 'cavalry', (4, 5), 'ukiyo-e-R-portrait')
        save_scene(modern(), 'modern', 'armor', (4, 5), 'ukiyo-e-W-portrait')
        save_scene(asian_medieval(), 'asian-medieval', 'rider', (4, 5), 'ukiyo-e-portrait')
    if 'all' in cmd:
        build_all()
