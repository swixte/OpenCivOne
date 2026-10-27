import math, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont, ImageChops
import comp, comp2
from comp import CELL, SIDE, W, H, COLS, ROWS

OUT = '/opt/cursor/artifacts/moodboard/ukiyo-states'
os.makedirs(OUT, exist_ok=True)
MW, MH = W - SIDE, H
BLUE, RED = (34, 92, 226), (206, 40, 30)
CREAM, INK, GOLD = (242, 233, 208), (18, 16, 14), (226, 176, 40)
SERIF = '/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf'
SANS = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'
GROUND, PLATE, VIEW = comp2.ground_and_plate()
UNIT_FIT = {'rider': 0.82, 'foot': 0.74, 'settler': 0.68, 'ship': 0.68}
UNITS = {k: (f'ukiyo-e-M-{k}', f) for k, f in UNIT_FIT.items()}
UNITS.update({
    'legion': ('ukiyo-e-R-legion', 0.76), 'cavalry': ('ukiyo-e-R-cavalry', 0.8), 'rsettler': ('ukiyo-e-R-settler', 0.68), 'trireme': ('ukiyo-e-R-trireme', 0.7),
    'armor': ('ukiyo-e-W-armor', 0.7), 'mech': ('ukiyo-e-W-mech', 0.7), 'artillery': ('ukiyo-e-W-artillery', 0.7), 'bomber': ('ukiyo-e-W-bomber', 0.66),
    'battleship': ('ukiyo-e-W-battleship', 0.76), 'transport': ('ukiyo-e-W-transport', 0.72),
    'qin': ('ukiyo-e-E-asia-ancient-unit', 0.74), 'meiji': ('ukiyo-e-E-asia-ind-unit', 0.74), 'atank': ('ukiyo-e-E-asia-mod-unit', 0.7),
    'knight': ('ukiyo-e-E-west-med-unit', 0.8), 'rifle': ('ukiyo-e-E-west-ind-unit', 0.74),
})
AIR = {'bomber'}
FLAGGED = {'armor', 'mech', 'battleship', 'transport', 'atank'}


def sashimono(colour, s=4):
    w, h = 20, 34
    im = Image.new('RGBA', (w * s, h * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rectangle((0, 0, 3 * s, h * s - 1), fill=INK + (255,))
    d.rectangle((1 * s, 1 * s, 2 * s, h * s - 2 * s), fill=(150, 110, 60, 255))
    d.rectangle((3 * s, 1 * s, w * s - 1, 21 * s), fill=INK + (255,))
    d.rectangle((5 * s, 3 * s, w * s - 1 - 2 * s, 19 * s), fill=colour + (255,))
    cx, cy, r = (5 + (w - 7) / 2) * s, 11 * s, 4.2 * s
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=CREAM + (255,))
    d.ellipse((cx - r * 0.45, cy - r * 0.45, cx + r * 0.45, cy + r * 0.45), fill=colour + (255,))
    return im.resize((w, h), Image.LANCZOS)
CITIES = {'castle': ('ukiyo-e-A-castle', 0.82), 'town': ('ukiyo-e-A-town', 0.78), 'rcity': ('ukiyo-e-R-city', 0.82), 'rtown': ('ukiyo-e-R-town', 0.78),
          'wcity': ('ukiyo-e-W-city', 0.82), 'wcity2': ('ukiyo-e-W-city2', 0.82), 'han': ('ukiyo-e-E-asia-ancient-city', 0.8),
          'tokyo': ('ukiyo-e-E-asia-ind-city', 0.8), 'acity': ('ukiyo-e-E-asia-mod-city', 0.82), 'gothic': ('ukiyo-e-E-west-med-city', 0.8),
          'port': ('ukiyo-e-E-west-ind-city', 0.8)}
CITY_FIT = {'castle': 0.82, 'town': 0.78}
rng = np.random.default_rng(5)


# ---------- sprites ----------
_cache = {}


def keyed(name):
    path = f'{comp.WORK}/{name}.png'
    if not os.path.exists(path):
        src = Image.open(f'{comp.RAW}/{name}.png').convert('RGB')
        a = np.asarray(src, dtype=np.float64)
        corners = np.concatenate([a[:16, :16].reshape(-1, 3), a[-16:, -16:].reshape(-1, 3), a[:16, -16:].reshape(-1, 3), a[-16:, :16].reshape(-1, 3)])
        bg = np.median(corners, 0)
        rgba = comp.key_out(src)
        if bg[2] < 240 or bg[0] < 240:
            sc = a.copy()
            sc[..., 0] *= 250 / max(bg[0], 1)
            sc[..., 2] *= 250 / max(bg[2], 1)
            alt = comp.key_out(Image.fromarray(np.uint8(np.clip(sc, 0, 255))))
            rgba[..., 3] = np.minimum(rgba[..., 3], alt[..., 3])
        rgba = comp.trim(rgba)
        Image.fromarray(np.rint(rgba).astype(np.uint8), 'RGBA').save(path)
    return Image.open(path)


def recolour(img, colour):
    """The green key (banner, crest disc) in the player's colour, keeping its shading."""
    a = np.asarray(img, dtype=np.float64).copy()
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    t = np.clip((g - np.maximum(r, b) - 25) / 60, 0, 1)
    shade = np.clip(g / 185, 0, 1.2)
    for k in range(3):
        a[..., k] = a[..., k] * (1 - t) + np.clip(colour[k] * shade, 0, 255) * t
    return Image.fromarray(np.uint8(np.clip(a, 0, 255)), 'RGBA')


def sprite(name, fit_px, colour=None):
    key = (name, round(fit_px), colour)
    if key not in _cache:
        img = keyed(name)
        if colour:
            img = recolour(img, colour)
        k = fit_px / max(img.size)
        img = img.resize((max(1, round(img.width * k)), max(1, round(img.height * k))), Image.LANCZOS)
        rgb, al = img.convert('RGB'), img.getchannel('A')
        rgb = ImageEnhance.Color(rgb).enhance(1.12)
        rgb = ImageEnhance.Contrast(rgb).enhance(1.1)
        rgb = rgb.filter(ImageFilter.UnsharpMask(1.0, 70, 2))
        out = rgb.convert('RGBA')
        out.putalpha(al)
        _cache[key] = out
    return _cache[key]


def faded(spr):
    """Sentry: the print in grey, paler, like a weak impression (the game greys sentried units)."""
    g = ImageEnhance.Color(spr.convert('RGB')).enhance(0)
    g = Image.blend(g, Image.new('RGB', g.size, (226, 220, 204)), 0.38)
    out = g.convert('RGBA')
    out.putalpha(spr.getchannel('A'))
    return out


def dimmed(spr):
    """No moves left: same colours, darker and flatter."""
    d = ImageEnhance.Brightness(spr.convert('RGB')).enhance(0.55)
    d = ImageEnhance.Color(d).enhance(0.7)
    out = d.convert('RGBA')
    out.putalpha(spr.getchannel('A').point(lambda v: int(v * 0.9)))
    return out


# ---------- drawing helpers ----------
def shadow_layer(canvas, spr, x, y, strength=0.55):
    pad = 12
    big = Image.new('L', (spr.width + 2 * pad, spr.height + 2 * pad), 0)
    big.paste(spr.getchannel('A'), (pad, pad))
    sh = big.filter(ImageFilter.GaussianBlur(4)).point(lambda v: int(v * strength))
    canvas.paste((10, 8, 5, 255), (x - pad + 5, y - pad + 6), sh)


def outline_layer(canvas, spr, x, y, colour=CREAM, size=5):
    pad = 6
    big = Image.new('L', (spr.width + 2 * pad, spr.height + 2 * pad), 0)
    big.paste(spr.getchannel('A'), (pad, pad))
    ring = big.filter(ImageFilter.MaxFilter(size)).filter(ImageFilter.GaussianBlur(0.6))
    canvas.paste(colour + (255,), (x - pad, y - pad), ring)


def glow_layer(canvas, spr, x, y, strength=1.0):
    pad = 24
    big = Image.new('L', (spr.width + 2 * pad, spr.height + 2 * pad), 0)
    big.paste(spr.getchannel('A'), (pad, pad))
    g = big.filter(ImageFilter.MaxFilter(13)).filter(ImageFilter.GaussianBlur(7)).point(lambda v: int(min(255, v * 1.3 * strength)))
    canvas.paste((255, 246, 214, 255), (x - pad, y - pad), g)


def text_img(txt, size, colour, font=SERIF):
    f = ImageFont.truetype(font, size)
    bb = f.getbbox(txt)
    im = Image.new('RGBA', (bb[2] - bb[0] + 2, bb[3] - bb[1] + 2), (0, 0, 0, 0))
    ImageDraw.Draw(im).text((1 - bb[0], 1 - bb[1]), txt, font=f, fill=colour)
    return im


def city_badge(size, colour, s=4):
    w, h = 36, 30
    im = Image.new('RGBA', (w * s, h * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((0, 0, w * s - 1, h * s - 1), 4 * s, fill=INK + (255,))
    d.rounded_rectangle((2 * s, 2 * s, w * s - 1 - 2 * s, h * s - 1 - 2 * s), 3 * s, fill=CREAM + (255,))
    d.rounded_rectangle((4 * s, 4 * s, w * s - 1 - 4 * s, h * s - 1 - 4 * s), 2 * s, fill=colour + (255,))
    im = im.resize((w, h), Image.LANCZOS)
    t = text_img(str(size), 21, CREAM)
    im.alpha_composite(t, ((w - t.width) // 2, (h - t.height) // 2))
    return im


def count_seal(n, s=4):
    """Unit count: a cream seal with a black number, like a printer's cartouche."""
    w = h = 28
    im = Image.new('RGBA', (w * s, h * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((0, 0, w * s - 1, h * s - 1), 5 * s, fill=INK + (255,))
    d.rounded_rectangle((2 * s, 2 * s, w * s - 1 - 2 * s, h * s - 1 - 2 * s), 4 * s, fill=CREAM + (255,))
    d.rounded_rectangle((4 * s, 4 * s, w * s - 1 - 4 * s, h * s - 1 - 4 * s), 3 * s, outline=INK + (255,), width=s)
    im = im.resize((w, h), Image.LANCZOS)
    t = text_img(str(n), 18, INK)
    im.alpha_composite(t, ((w - t.width) // 2, (h - t.height) // 2))
    return im


def star(size=20, s=4):
    im = Image.new('RGBA', (size * s, size * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    c = size * s / 2
    def pts(r_out, r_in):
        p = []
        for i in range(10):
            ang = -math.pi / 2 + i * math.pi / 5
            r = r_out if i % 2 == 0 else r_in
            p.append((c + r * math.cos(ang), c + r * math.sin(ang)))
        return p
    d.polygon(pts(c, c * 0.48), fill=INK + (255,))
    d.polygon(pts(c * 0.72, c * 0.33), fill=GOLD + (255,))
    return im.resize((size, size), Image.LANCZOS)


def capital_mon(size=22, s=4):
    im = Image.new('RGBA', (size * s, size * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    S = size * s
    d.ellipse((0, 0, S - 1, S - 1), fill=INK + (255,))
    d.ellipse((2 * s, 2 * s, S - 1 - 2 * s, S - 1 - 2 * s), fill=GOLD + (255,))
    d.ellipse((S * 0.3, S * 0.3, S * 0.7, S * 0.7), fill=(196, 44, 30, 255))
    for i in range(8):
        ang = i * math.pi / 4
        x0, y0 = S / 2 + math.cos(ang) * S * 0.24, S / 2 + math.sin(ang) * S * 0.24
        x1, y1 = S / 2 + math.cos(ang) * S * 0.4, S / 2 + math.sin(ang) * S * 0.4
        d.line((x0, y0, x1, y1), fill=INK + (255,), width=s)
    return im.resize((size, size), Image.LANCZOS)


def brush_frame(cx0, cy0, seed=3, colour=(196, 44, 30)):
    """A cinnabar brush-stroke frame around a cell: strokes of varying width with dry-brush breaks."""
    s = 3
    im = Image.new('RGBA', (CELL * s, CELL * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    r = np.random.default_rng(seed)
    m = 5 * s
    sides = [((m, m), (CELL * s - m, m)), ((CELL * s - m, m), (CELL * s - m, CELL * s - m)),
             ((CELL * s - m, CELL * s - m), (m, CELL * s - m)), ((m, CELL * s - m), (m, m))]
    for (x0, y0), (x1, y1) in sides:
        n = 60
        for i in range(n):
            t0, t1 = i / n, (i + 1) / n
            w = (3.2 + 2.6 * math.sin(math.pi * t0) + r.normal(0, 0.5)) * s
            if r.random() < 0.06:
                continue
            p0 = (x0 + (x1 - x0) * t0 + r.normal(0, 0.4) * s, y0 + (y1 - y0) * t0 + r.normal(0, 0.4) * s)
            p1 = (x0 + (x1 - x0) * t1, y0 + (y1 - y0) * t1)
            d.line((p0, p1), fill=colour + (235,), width=max(2, int(w)))
    return im.resize((CELL, CELL), Image.LANCZOS)


def yellow_square(d, c, r):
    x0, y0 = c * CELL, r * CELL
    d.rectangle((x0 + 1, y0 + 1, x0 + CELL - 2, y0 + CELL - 2), outline=(20, 16, 0), width=5)
    d.rectangle((x0 + 2, y0 + 2, x0 + CELL - 3, y0 + CELL - 3), outline=(255, 226, 0), width=3)


def centre(c, r):
    return c * CELL + CELL / 2, r * CELL + CELL / 2


def draw_polyline(layer, pts, width, fill, s=2):
    d = ImageDraw.Draw(layer)
    sp = [(x * s, y * s) for x, y in pts]
    d.line(sp, fill=fill, width=width * s, joint='curve')
    for x, y in sp:
        d.ellipse((x - width * s / 2, y - width * s / 2, x + width * s / 2, y + width * s / 2), fill=fill)


def roads(canvas, paths, rail=False):
    s = 2
    layer = Image.new('RGBA', (MW * s, MH * s), (0, 0, 0, 0))
    for cells in paths:
        pts = [centre(*c) for c in cells]
        if not rail:
            draw_polyline(layer, pts, 13, INK + (235,))
            draw_polyline(layer, pts, 9, (214, 182, 118, 255))
            d = ImageDraw.Draw(layer)
            for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
                L = math.hypot(x1 - x0, y1 - y0)
                for k in range(int(L // 14)):
                    t0, t1 = k * 14 / L, (k * 14 + 6) / L
                    d.line(((x0 + (x1 - x0) * t0) * s, (y0 + (y1 - y0) * t0) * s, (x0 + (x1 - x0) * t1) * s, (y0 + (y1 - y0) * t1) * s), fill=(150, 116, 64, 255), width=2 * s)
        else:
            d = ImageDraw.Draw(layer)
            for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
                L = math.hypot(x1 - x0, y1 - y0)
                ux, uy = (x1 - x0) / L, (y1 - y0) / L
                px, py = -uy, ux
                for k in range(int(L // 10) + 1):
                    cx, cy = x0 + ux * k * 10, y0 + uy * k * 10
                    d.line(((cx - px * 9) * s, (cy - py * 9) * s, (cx + px * 9) * s, (cy + py * 9) * s), fill=INK + (255,), width=4 * s)
                    d.line(((cx - px * 8) * s, (cy - py * 8) * s, (cx + px * 8) * s, (cy + py * 8) * s), fill=(120, 84, 48, 255), width=2 * s)
                for off in (-4.5, 4.5):
                    d.line(((x0 + px * off) * s, (y0 + py * off) * s, (x1 + px * off) * s, (y1 + py * off) * s), fill=INK + (255,), width=3 * s)
    canvas.alpha_composite(layer.resize((MW, MH), Image.LANCZOS))


def irrigation(canvas, cells):
    """Irrigated fields: a soft field patch, three short ditches with breaks, rows of crops between."""
    s = 2
    field = Image.new('L', (MW, MH), 0)
    fd = ImageDraw.Draw(field)
    for c, r in cells:
        fd.rounded_rectangle((c * CELL + 10, r * CELL + 12, (c + 1) * CELL - 10, (r + 1) * CELL - 10), 18, fill=255)
    n = comp.noise(MW, MH, (6, 6), 61)
    fa = np.asarray(field.filter(ImageFilter.GaussianBlur(5)), dtype=np.float64) / 255 * (0.75 + 0.25 * n)
    base = np.asarray(canvas.convert('RGB'), dtype=np.float64)
    tint = np.array([150, 168, 84], dtype=np.float64)
    out = base * (1 - fa[..., None] * 0.5) + tint * fa[..., None] * 0.5
    canvas.paste(Image.fromarray(np.uint8(np.clip(out, 0, 255))).convert('RGBA'))
    layer = Image.new('RGBA', (MW * s, MH * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    r_ = np.random.default_rng(62)
    for c, r in cells:
        x0, x1 = c * CELL + 20, (c + 1) * CELL - 20
        for i in range(3):
            y = r * CELL + 34 + i * 32
            segs = [(x0, x0 + 38), (x0 + 46, x1)] if i % 2 == 0 else [(x0, x1 - 44), (x1 - 36, x1)]
            for a0, a1 in segs:
                pts = [(x * s, (y + math.sin(x / 9 + i) * 1.2) * s) for x in np.linspace(a0, a1, 8)]
                d.line(pts, fill=INK + (220,), width=5 * s)
                d.line(pts, fill=(70, 120, 186, 255), width=3 * s)
            for k in range(7):
                xx = x0 + 4 + k * (x1 - x0 - 8) / 6 + r_.normal(0, 1)
                yy = y + 16
                d.line(((xx - 3) * s, (yy + 2) * s, xx * s, (yy - 3) * s, (xx + 3) * s, (yy + 2) * s), fill=(70, 96, 30, 235), width=2 * s)
    canvas.alpha_composite(layer.resize((MW, MH), Image.LANCZOS))


def fog(canvas, cells):
    """Unexplored land as blank cream paper, with a soft, irregular printed edge and a faint ink rim."""
    m = Image.new('L', (MW, MH), 0)
    d = ImageDraw.Draw(m)
    for c, r in cells:
        d.rectangle((c * CELL, r * CELL, (c + 1) * CELL - 1, (r + 1) * CELL - 1), fill=255)
    b = np.asarray(m.filter(ImageFilter.GaussianBlur(20)), dtype=np.float64) / 255
    n = comp.noise(MW, MH, (26, 26), 41) * 0.5 + comp.noise(MW, MH, (9, 9), 42) * 0.3 + comp.noise(MW, MH, (3, 3), 44) * 0.2
    t = b + (n - 0.5) * 0.5
    a = np.clip((t - 0.3) / 0.22, 0, 1)
    a = a * a * (3 - 2 * a)
    paper = np.asarray(comp.woodgrain(MH, MW, (244, 237, 216), (232, 220, 192), period=18, warp=8, strength=0.35, seed=43).rotate(90, expand=True), dtype=np.float64)
    rim = np.exp(-((a - 0.62) / 0.12) ** 2)
    paper = paper * (1 - rim[..., None] * 0.22)
    base = np.asarray(canvas.convert('RGB'), dtype=np.float64)
    out = base * (1 - a[..., None]) + paper * a[..., None]
    canvas.paste(Image.fromarray(np.uint8(np.clip(out, 0, 255))).convert('RGBA'))


def goto_path(canvas, cells, start_gap=46):
    layer = Image.new('RGBA', (MW * 2, MH * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    pts = [centre(*c) for c in cells]
    r = np.random.default_rng(9)
    dist = 0
    dabs = []
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        L = math.hypot(x1 - x0, y1 - y0)
        k = 0
        while k < L:
            if dist + k > start_gap and (dist + k) < sum(math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(pts, pts[1:])) - 26:
                dabs.append((x0 + (x1 - x0) * k / L, y0 + (y1 - y0) * k / L))
            k += 17
        dist += L
    for x, y in dabs:
        rr = 5.5 + r.normal(0, 0.5)
        d.ellipse(((x - rr - 2) * 2, (y - rr * 0.8 - 2) * 2, (x + rr + 2) * 2, (y + rr * 0.8 + 2) * 2), fill=CREAM + (220,))
        d.ellipse(((x - rr) * 2, (y - rr * 0.8) * 2, (x + rr) * 2, (y + rr * 0.8) * 2), fill=(24, 22, 30, 240))
    x, y = pts[-1]
    for wdt, col in ((16, CREAM + (220,)), (9, (24, 22, 30, 245))):
        d.arc(((x - 22) * 2, (y - 17) * 2, (x + 22) * 2, (y + 17) * 2), 200, 520, fill=col, width=wdt)
    canvas.alpha_composite(layer.resize((MW, MH), Image.LANCZOS))


# ---------- the scene ----------
class Scene:
    def __init__(self, outline=True):
        self.outline = outline
        self.boxes = []
        self.canvas = GROUND.convert('RGBA')

    def keep(self, c, r, x, y, w, h, what):
        self.boxes.append((c, r, x, y, x + w, y + h, what))

    def place(self, spr, c, r, dx=0, dy=0, outline=None, shadow=True, what=''):
        cx, cy = centre(c, r)
        x, y = round(cx - spr.width / 2 + dx), round(cy - spr.height / 2 + dy)
        if shadow:
            shadow_layer(self.canvas, spr, x, y)
        if outline if outline is not None else self.outline:
            outline_layer(self.canvas, spr, x, y)
        self.canvas.alpha_composite(spr, (x, y))
        self.keep(c, r, x, y, spr.width, spr.height, what)
        return x, y

    def mark(self, img, c, r, x, y, what):
        x = min(max(x, c * CELL + 3), (c + 1) * CELL - 3 - img.width)
        y = min(max(y, r * CELL + 3), (r + 1) * CELL - 3 - img.height)
        self.canvas.alpha_composite(img, (x, y))
        self.keep(c, r, x, y, img.width, img.height, what)

    def obj(self, name, c, r, fit=0.5, dx=0, dy=0):
        full = name if name.startswith('ukiyo-e-') else f'ukiyo-e-M-{name}'
        self.place(sprite(full, CELL * fit), c, r, dx, dy, outline=False, what=name)

    def city(self, kind, c, r, owner='p', size=3, walls=False, capital=False, garrison=0, disorder=False):
        col = BLUE if owner == 'p' else RED
        cname, fit = CITIES[kind]
        if walls:
            ring = sprite('ukiyo-e-M-walls', CELL * 0.9)
            x, y = self.place(ring, c, r, outline=False, what='walls')
            fit = 0.66
        spr = sprite(cname, CELL * fit)
        sx, sy = self.place(spr, c, r, dy=-4 if walls else 0, outline=False, what=kind)
        if walls:
            front = ring.crop((0, int(ring.height * 0.62), ring.width, ring.height))
            self.canvas.alpha_composite(front, (x, y + int(ring.height * 0.62)))
        if disorder:
            sm = sprite('ukiyo-e-M-smoke', CELL * 0.84)
            sm = ImageEnhance.Contrast(ImageEnhance.Brightness(sm.convert('RGB')).enhance(0.8)).enhance(1.25).convert('RGBA')
            sm.putalpha(sprite('ukiyo-e-M-smoke', CELL * 0.84).getchannel('A'))
            sx2 = c * CELL + CELL - sm.width - 4
            sy2 = r * CELL + 6
            self.canvas.alpha_composite(sm, (sx2, sy2))
            self.keep(c, r, sx2, sy2, sm.width, sm.height, 'smoke')
        b = city_badge(size, col)
        self.mark(b, c, r, c * CELL + 4, r * CELL + 4, 'badge')
        if capital:
            self.mark(capital_mon(28), c, r, c * CELL + 4 + b.width + 3, r * CELL + 5, 'capital')
        if garrison:
            self.mark(count_seal(garrison), c, r, c * CELL + CELL - 32, r * CELL + CELL - 32, 'garrison')

    def unit(self, kind, c, r, owner='p', state=(), stack=1, stack_style='badge', cargo=0, select=None, outline=None):
        col = BLUE if owner == 'p' else RED
        uname, ufit = UNITS[kind]
        spr = sprite(uname, CELL * ufit, col)
        if 'sentry' in state:
            spr = faded(spr)
        if 'nomoves' in state:
            spr = dimmed(spr)
        dx = dy = 0
        copies = 0
        if stack > 1 and stack_style in ('offset', 'both'):
            copies = 1 if stack_style == 'both' else min(stack - 1, 2)
            dx = dy = -copies * 7 / 2
        if 'fortified' in state:
            ring = sprite('ukiyo-e-M-fort', CELL * 0.9)
            fx, fy = self.place(ring, c, r, dy=10, outline=False, what='fort')
            dy -= 8
        cx, cy = centre(c, r)
        x, y = round(cx - spr.width / 2 + dx), round(cy - spr.height / 2 + dy)
        if select == 'glow':
            glow_layer(self.canvas, spr, x, y)
        for k in range(copies, 0, -1):
            back = ImageEnhance.Brightness(spr.convert('RGB')).enhance(0.78).convert('RGBA')
            back.putalpha(spr.getchannel('A'))
            self.place(back, c, r, dx + k * 7, dy + k * 7, outline=outline, what='copy')
        if kind in AIR:
            dx -= 6
            dy -= 10
            x, y = round(cx - spr.width / 2 + dx), round(cy - spr.height / 2 + dy)
            pad = 14
            big = Image.new('L', (spr.width + 2 * pad, spr.height + 2 * pad), 0)
            big.paste(spr.getchannel('A'), (pad, pad))
            sh = big.filter(ImageFilter.GaussianBlur(5)).point(lambda v: int(v * 0.28))
            self.canvas.paste((10, 8, 5, 255), (x - pad + 16, y - pad + 20), sh)
        self.place(spr, c, r, dx, dy, outline=outline, shadow=copies == 0 and kind not in AIR, what=kind)
        if 'fortified' in state:
            front = ring.crop((0, int(ring.height * 0.55), ring.width, ring.height))
            self.canvas.alpha_composite(front, (fx, fy + int(ring.height * 0.55)))
        if kind in FLAGGED:
            self.mark(sashimono(col), c, r, x - 8, y - 18, 'flag')
        if 'veteran' in state:
            self.mark(star(26), c, r, x + spr.width - 4, y - 6, 'veteran')
        n = cargo or (stack if stack > 1 and stack_style in ('badge', 'both') else 0)
        if n:
            sl = count_seal(n)
            self.mark(sl, c, r, x + spr.width - 16, y + spr.height - 20, 'count')
        if select == 'yellow':
            yellow_square(ImageDraw.Draw(self.canvas), c, r)
        elif select == 'brush':
            self.canvas.alpha_composite(brush_frame(c, r), (c * CELL, r * CELL))

    def burst(self, c, r):
        b = sprite('ukiyo-e-M-burst', CELL * 0.8)
        self.place(b, c, r, outline=False, shadow=False, what='burst')

    def check(self):
        for c, r, x0, y0, x1, y1, what in self.boxes:
            assert c * CELL <= x0 and x1 <= (c + 1) * CELL and r * CELL <= y0 and y1 <= (r + 1) * CELL, (what, c, r, x0, y0, x1, y1)

    def screen(self, sel_kind='rider', sel_cell=(4, 5), grid=False, portrait='ukiyo-e-portrait'):
        self.check()
        m = comp2.with_grid(self.canvas) if grid else self.canvas
        portrait = Image.open(f'{comp.RAW}/{portrait}.png').convert('RGB')
        us = recolour(keyed(UNITS[sel_kind][0]), BLUE)
        c, r = sel_cell
        side = comp.sidebar('ukiyo-e', PLATE, VIEW, portrait, us, GROUND.crop((c * CELL, r * CELL, (c + 1) * CELL, (r + 1) * CELL)))
        full = Image.new('RGB', (W, H))
        full.paste(side, (0, 0))
        full.paste(m.convert('RGB'), (SIDE, 0))
        return full
