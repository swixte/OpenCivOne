"""Second pass: improvements printed into the ground, grounded objects, ships in water, airborne planes, dominant cities."""
import math
import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageChops
import comp
import states
from states import (CELL, MW, MH, BLUE, RED, CREAM, INK, GOLD, sprite, keyed, recolour, centre, shadow_layer, outline_layer,
                    city_badge, capital_mon, count_seal, UNITS, CITIES)

CITY_UP = 32          # px a city may rise into the cell above (generate.py CITY_UP = 8 of a 32 px tile)
SHIPS = {'ship', 'trireme', 'battleship', 'transport'}
_tex = {}


def texture(name):
    """A generated top-down texture laid over the whole map in map coordinates (mirrored to cover it), toned to the ground."""
    if name not in _tex:
        t = Image.open(f'{comp.RAW}/ukiyo-e-{name}.png').convert('RGB').resize((768, 768), Image.LANCZOS)
        big = Image.new('RGB', (MW, MH))
        T = Image.Transpose
        for i in range(0, MW, 768):
            for j in range(0, MH, 768):
                tt = t
                if (i // 768) % 2:
                    tt = tt.transpose(T.FLIP_LEFT_RIGHT)
                if (j // 768) % 2:
                    tt = tt.transpose(T.FLIP_TOP_BOTTOM)
                big.paste(tt, (i, j))
        big = comp.grade(big, 0.86, 0.92, 1.0)
        _tex[name] = big
    return _tex[name]


def cells_mask(cells, inset=7, wobble=3.0, seed=1, soft=1.6):
    """Union of whole cells, shrunk at its outer boundary only (so neighbours join), with an irregular printed edge."""
    m = Image.new('L', (MW, MH), 0)
    d = ImageDraw.Draw(m)
    for c, r in cells:
        d.rectangle((c * CELL, r * CELL, (c + 1) * CELL - 1, (r + 1) * CELL - 1), fill=255)
    m = m.filter(ImageFilter.MinFilter(2 * inset + 1))
    b = np.asarray(m.filter(ImageFilter.GaussianBlur(3)), dtype=np.float64) / 255
    n = comp.noise(MW, MH, (7, 7), seed) * 0.6 + comp.noise(MW, MH, (3, 3), seed + 1) * 0.4
    t = b + (n - 0.5) * wobble / 10
    a = np.clip((t - 0.5) * 4 + 0.5, 0, 1)
    img = Image.fromarray(np.uint8(a * 255)).filter(ImageFilter.GaussianBlur(soft))
    # keep strictly inside the cells
    hard = Image.new('L', (MW, MH), 0)
    hd = ImageDraw.Draw(hard)
    for c, r in cells:
        hd.rectangle((c * CELL + 2, r * CELL + 2, (c + 1) * CELL - 3, (r + 1) * CELL - 3), fill=255)
    hard = hard.filter(ImageFilter.MaxFilter(5))
    return ImageChops.multiply(img, hard)


def blend_in(canvas, img, mask, keyline=True):
    base = canvas.convert('RGB')
    out = Image.composite(img, base, mask)
    if keyline:
        a = np.asarray(mask, dtype=np.float64) / 255
        ring = np.exp(-((a - 0.5) / 0.18) ** 2)
        o = np.asarray(out, dtype=np.float64) * (1 - ring[..., None] * 0.55) + np.array(INK) * ring[..., None] * 0.55
        out = Image.fromarray(np.uint8(np.clip(o, 0, 255)))
    canvas.paste(out.convert('RGBA'))


def farmland(canvas, cells, kind='T-paddy', channel=None, seed=1):
    """Fields replace the ground inside the farmed cells; neighbouring farmed cells make one block."""
    m = cells_mask(cells, seed=seed)
    tex = texture(kind)
    blend_in(canvas, tex, m)
    if channel:
        layer = Image.new('RGBA', (MW * 2, MH * 2), (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        pts = [(x * 2, y * 2) for x, y in channel]
        d.line(pts, fill=INK + (235,), width=12, joint='curve')
        d.line(pts, fill=(96, 150, 200, 255), width=7, joint='curve')
        d.line([(x, y - 2) for x, y in pts], fill=(226, 238, 244, 170), width=2)
        layer = layer.resize((MW, MH), Image.LANCZOS)
        layer.putalpha(ImageChops.multiply(layer.getchannel('A'), m.point(lambda v: 255 if v > 90 else 0)))
        canvas.alpha_composite(layer)


def _wobbly(pts, amp, seed, step=10):
    r = np.random.default_rng(seed)
    out = []
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        L = math.hypot(x1 - x0, y1 - y0)
        n = max(2, int(L // step))
        px, py = -(y1 - y0) / L, (x1 - x0) / L
        for k in range(n):
            t = k / n
            w = math.sin(t * math.pi) * r.normal(0, amp)
            out.append((x0 + (x1 - x0) * t + px * w, y0 + (y1 - y0) * t + py * w))
    out.append(pts[-1])
    return out


def road(canvas, paths, seed=3):
    """A narrow worn path printed into the ground, centre to centre."""
    s = 3
    layer = Image.new('RGBA', (MW * s, MH * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for i, cells in enumerate(paths):
        pts = _wobbly([centre(*c) for c in cells], 1.6, seed + i)
        sp = [(x * s, y * s) for x, y in pts]
        d.line(sp, fill=(58, 42, 24, 170), width=8 * s, joint='curve')
        d.line(sp, fill=(226, 206, 156, 235), width=5 * s, joint='curve')
        d.line([(x + 0.6 * s, y + 0.6 * s) for x, y in sp], fill=(196, 168, 112, 150), width=2 * s, joint='curve')
    canvas.alpha_composite(layer.resize((MW, MH), Image.LANCZOS))


def railroad(canvas, paths):
    """A fine double rail with ties, about a tenth of a cell wide."""
    s = 3
    layer = Image.new('RGBA', (MW * s, MH * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for cells in paths:
        pts = [centre(*c) for c in cells]
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            L = math.hypot(x1 - x0, y1 - y0)
            ux, uy = (x1 - x0) / L, (y1 - y0) / L
            px, py = -uy, ux
            for k in range(int(L // 6) + 1):
                cx, cy = x0 + ux * k * 6, y0 + uy * k * 6
                d.line(((cx - px * 6.5) * s, (cy - py * 6.5) * s, (cx + px * 6.5) * s, (cy + py * 6.5) * s), fill=(74, 52, 32, 255), width=int(2.2 * s))
            for off in (-3.2, 3.2):
                d.line(((x0 + px * off) * s, (y0 + py * off) * s, (x1 + px * off) * s, (y1 + py * off) * s), fill=(30, 30, 36, 255), width=int(1.6 * s))
    canvas.alpha_composite(layer.resize((MW, MH), Image.LANCZOS))


def pollution(canvas, c, r, seed=5):
    """An oily stain worked into the ground, with a thin wisp of smoke."""
    m = Image.new('L', (MW, MH), 0)
    ImageDraw.Draw(m).ellipse((c * CELL + 16, r * CELL + 30, (c + 1) * CELL - 14, (r + 1) * CELL - 14), fill=255)
    b = np.asarray(m.filter(ImageFilter.GaussianBlur(10)), dtype=np.float64) / 255
    n = comp.noise(MW, MH, (6, 6), seed) * 0.6 + comp.noise(MW, MH, (2, 2), seed + 1) * 0.4
    a = np.clip((b + (n - 0.5) * 0.7 - 0.35) * 2.2, 0, 0.82)
    hard = np.zeros((MH, MW))
    hard[r * CELL + 3:(r + 1) * CELL - 3, c * CELL + 3:(c + 1) * CELL - 3] = 1
    a *= hard
    base = np.asarray(canvas.convert('RGB'), dtype=np.float64)
    stain = np.array([42, 34, 26], dtype=np.float64)
    sheen = (n > 0.62) * 0.25
    out = base * (1 - a[..., None]) + stain * a[..., None] + np.array([70, 60, 90]) * (sheen * a)[..., None]
    canvas.paste(Image.fromarray(np.uint8(np.clip(out, 0, 255))).convert('RGBA'))
    sm = sprite('ukiyo-e-M-smoke', CELL * 0.5)
    sm2 = sm.copy()
    sm2.putalpha(sm.getchannel('A').point(lambda v: int(v * 0.7)))
    canvas.alpha_composite(sm2, (c * CELL + CELL // 2 - sm.width // 2 + 10, r * CELL + 6))


def ground_patch(canvas, cx, cy, rx, ry, colour, strength=0.55, seed=7):
    m = Image.new('L', (MW, MH), 0)
    ImageDraw.Draw(m).ellipse((cx - rx, cy - ry, cx + rx, cy + ry), fill=255)
    b = np.asarray(m.filter(ImageFilter.GaussianBlur(6)), dtype=np.float64) / 255
    n = comp.noise(MW, MH, (5, 5), seed)
    a = np.clip((b + (n - 0.5) * 0.5 - 0.3) * 2, 0, 1) * strength
    base = np.asarray(canvas.convert('RGB'), dtype=np.float64)
    out = base * (1 - a[..., None]) + np.array(colour, dtype=np.float64) * a[..., None]
    canvas.paste(Image.fromarray(np.uint8(np.clip(out, 0, 255))).convert('RGBA'))


def contact_shadow(canvas, cx, cy, rx, ry, alpha=110):
    layer = Image.new('RGBA', (MW, MH), (0, 0, 0, 0))
    ImageDraw.Draw(layer).ellipse((cx - rx, cy - ry, cx + rx, cy + ry), fill=(12, 10, 6, alpha))
    canvas.alpha_composite(layer.filter(ImageFilter.GaussianBlur(4)))


def toned(spr, canvas, x, y, amount=0.12, sat=0.9):
    """Pull a small object a little toward the ground colour under it, so it sits in the print."""
    under = canvas.crop((x, y, x + spr.width, y + spr.height)).convert('RGB').filter(ImageFilter.GaussianBlur(10))
    rgb = ImageEnhance.Color(spr.convert('RGB')).enhance(sat)
    rgb = Image.blend(rgb, under, amount)
    out = rgb.convert('RGBA')
    out.putalpha(spr.getchannel('A'))
    return out


def fade_base(spr, frac=0.14):
    a = np.asarray(spr.getchannel('A'), dtype=np.float64)
    h = a.shape[0]
    ramp = np.ones(h)
    k = int(h * frac)
    ramp[h - k:] = np.linspace(1, 0.35, k)
    out = spr.copy()
    out.putalpha(Image.fromarray(np.uint8(a * ramp[:, None])))
    return out


class Scene2(states.Scene):
    def grounded(self, name, c, r, fit=0.48, patch=(150, 120, 74), what='', dy=8, fade=0.14):
        full = name if name.startswith('ukiyo-e-') else f'ukiyo-e-M-{name}'
        spr = sprite(full, CELL * fit)
        cx, cy = centre(c, r)
        x, y = round(cx - spr.width / 2), round(cy - spr.height / 2 + dy)
        base_y = y + spr.height - 4
        ground_patch(self.canvas, cx, base_y - 6, spr.width * 0.58, max(10, spr.height * 0.26), patch, seed=c * 17 + r)
        contact_shadow(self.canvas, cx + 3, base_y - 2, spr.width * 0.42, max(5, spr.height * 0.12))
        spr = fade_base(toned(spr, self.canvas, x, y), fade)
        self.canvas.alpha_composite(spr, (x, y))
        self.keep(c, r, x, y, spr.width, spr.height, what or name)

    def mine(self, c, r):
        self.grounded('ukiyo-e-M-mine2', c, r, fit=0.6, patch=(120, 96, 66), what='mine', dy=6, fade=0.2)

    def hut(self, c, r):
        self.grounded('ukiyo-e-M-hut2', c, r, fit=0.5, patch=(170, 140, 90), what='hut')

    def horses(self, c, r):
        self.grounded('horses', c, r, fit=0.44, patch=(160, 150, 86), what='horses', fade=0.1)

    def fish(self, c, r):
        spr = sprite('ukiyo-e-M-fish', CELL * 0.42)
        cx, cy = centre(c, r)
        x, y = round(cx - spr.width / 2), round(cy - spr.height / 2)
        self.ripples(cx, y + spr.height - 8, spr.width * 0.5)
        self.canvas.alpha_composite(fade_base(spr, 0.25), (x, y))
        self.keep(c, r, x, y, spr.width, spr.height, 'fish')

    def ripples(self, cx, cy, rx, wake=0):
        s = 3
        layer = Image.new('RGBA', (int(rx * 2 + 60 + wake) * s, 70 * s), (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        ox, oy = 30 + rx, 35
        for k, (rr, wdt) in enumerate(((1.0, 3.2), (1.25, 2.2))):
            box = ((ox - rx * rr) * s, (oy - rx * rr * 0.22) * s, (ox + rx * rr) * s, (oy + rx * rr * 0.22) * s)
            for a0 in range(0, 360, 40):
                d.arc(box, a0 + k * 15, a0 + 26 + k * 15, fill=INK + (200,), width=int((wdt + 1.6) * s))
                d.arc(box, a0 + k * 15, a0 + 26 + k * 15, fill=(244, 238, 222, 255), width=int(wdt * s))
        if wake:
            for k, off in enumerate((-5, 4)):
                pts = [((ox + rx * 0.8 + t) * s, (oy + off * (1 + t / wake * 1.6)) * s) for t in np.linspace(0, wake, 8)]
                d.line(pts, fill=INK + (180,), width=int(4.2 * s))
                d.line(pts, fill=(244, 238, 222, 240), width=int(2.6 * s))
        img = layer.resize((layer.width // s, layer.height // s), Image.LANCZOS)
        self.canvas.alpha_composite(img, (round(cx - ox), round(cy - oy)))

    def unit(self, kind, c, r, owner='p', **kw):
        if kind in SHIPS:
            col = BLUE if owner == 'p' else RED
            spr = sprite(UNITS[kind][0], CELL * UNITS[kind][1] * 0.94, col)
            cx, cy = centre(c, r)
            water_y = cy + spr.height / 2 - 14
            # reflection: the hull flipped, squashed, darkened, broken by the waves
            ref = spr.transpose(Image.Transpose.FLIP_TOP_BOTTOM).resize((spr.width, max(4, int(spr.height * 0.3))))
            ref_rgb = ImageEnhance.Brightness(ImageEnhance.Color(ref.convert('RGB')).enhance(0.5)).enhance(0.55)
            ra = np.asarray(ref.getchannel('A'), dtype=np.float64) * 0.42
            rows = np.arange(ra.shape[0])
            ra *= (np.sin(rows * 1.7) > -0.3)[:, None] * np.linspace(1, 0.3, ra.shape[0])[:, None]
            ref_img = ref_rgb.convert('RGBA')
            ref_img.putalpha(Image.fromarray(np.uint8(ra)))
            rx_ = round(cx - spr.width / 2 - 8)
            self.canvas.alpha_composite(ref_img, (rx_, round(water_y - 2)))
            self.ripples(cx - 8, water_y + 2, spr.width * 0.42, wake=24)
            kw.setdefault('dy', -8)
            kw['dx'] = kw.get('dx', 0) - 8
            kw['_spr'] = spr
        return self._unit(kind, c, r, owner, **kw)

    def _unit(self, kind, c, r, owner='p', dx=0, dy=0, _spr=None, **kw):
        if kind in states.AIR:
            return self.aircraft(kind, c, r, owner, **kw)
        if _spr is None:
            return super().unit(kind, c, r, owner, **kw)
        # ship with its own pre-scaled sprite and offset
        saved = UNITS[kind]
        UNITS[kind] = (saved[0], _spr.height / CELL if _spr.height >= _spr.width else _spr.width / CELL)
        try:
            self._shift = (dx, dy)
            return super().unit(kind, c, r, owner, **kw)
        finally:
            UNITS[kind] = saved
            self._shift = (0, 0)

    def place(self, spr, c, r, dx=0, dy=0, **kw):
        sx, sy = getattr(self, '_shift', (0, 0))
        return super().place(spr, c, r, dx + sx, dy + sy, **kw)

    def aircraft(self, kind, c, r, owner='p', **kw):
        col = BLUE if owner == 'p' else RED
        spr = sprite(UNITS[kind][0], CELL * 0.6, col)
        cx, cy = centre(c, r)
        x, y = round(cx - spr.width / 2 - 12), round(cy - spr.height / 2 - 16)
        sh = spr.getchannel('A').resize((int(spr.width * 0.86), int(spr.height * 0.86))).filter(ImageFilter.GaussianBlur(2.2)).point(lambda v: int(v * 0.4))
        self.canvas.paste((14, 12, 8, 255), (x + 30, y + 40), sh)
        self.keep(c, r, x + 30, y + 40, sh.width, sh.height, 'air shadow')
        if self.outline:
            outline_layer(self.canvas, spr, x, y)
        self.canvas.alpha_composite(spr, (x, y))
        self.keep(c, r, x, y, spr.width, spr.height, kind)

    # ---------- cities ----------
    def city(self, kind, c, r, owner='p', size=3, capital=False, garrison=0, disorder=False, plate=True, **kw):
        col = BLUE if owner == 'p' else RED
        cname, _ = CITIES[kind]
        x0, y0 = c * CELL, r * CELL
        if plate:
            self.city_plate(c, r, col)
        img = keyed(cname)
        k = min(CELL * 0.94 / img.width, (CELL - 10 + CITY_UP) / img.height)
        spr = sprite(cname, max(img.width, img.height) * k)
        x = round(x0 + (CELL - spr.width) / 2)
        y = y0 + CELL - 7 - spr.height
        shadow_layer(self.canvas, spr, x, y, 0.7)
        outline_layer(self.canvas, spr, x, y, colour=INK, size=9)
        outline_layer(self.canvas, spr, x, y, colour=CREAM, size=5)
        self.canvas.alpha_composite(spr, (x, y))
        self.boxes.append((c, r, x, max(y, y0), x + spr.width, y + spr.height, kind))
        assert y >= y0 - CITY_UP, (kind, y, y0)
        if disorder:
            sm = sprite('ukiyo-e-M-smoke', CELL * 0.8)
            self.canvas.alpha_composite(sm, (x0 + CELL - sm.width - 6, y0 + 4))
        b = city_badge(size, col)
        self.mark(b, c, r, x0 + 4, y0 + 4, 'badge')
        if capital:
            self.mark(capital_mon(28), c, r, x0 + 4 + b.width + 3, y0 + 5, 'capital')
        if garrison:
            self.mark(count_seal(garrison), c, r, x0 + CELL - 32, y0 + CELL - 32, 'garrison')

    def city_plate(self, c, r, col):
        """The city's ground: paved, trodden earth inside the cell, with a band in the owner's colour (the game fills the city square with it)."""
        x0, y0 = c * CELL, r * CELL
        s = 3
        layer = Image.new('RGBA', (CELL * s, CELL * s), (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        m = 4
        d.rounded_rectangle((m * s, (m + 10) * s, (CELL - m) * s, (CELL - m) * s), 10 * s, fill=INK + (255,))
        d.rounded_rectangle(((m + 2) * s, (m + 12) * s, (CELL - m - 2) * s, (CELL - m - 2) * s), 9 * s, fill=col + (255,))
        d.rounded_rectangle(((m + 7) * s, (m + 17) * s, (CELL - m - 7) * s, (CELL - m - 7) * s), 6 * s, fill=CREAM + (255,))
        d.rounded_rectangle(((m + 9) * s, (m + 19) * s, (CELL - m - 9) * s, (CELL - m - 9) * s), 5 * s, fill=(206, 184, 136, 255))
        r_ = np.random.default_rng(c * 31 + r)
        for _ in range(70):
            px, py = r_.uniform(m + 12, CELL - m - 12) * s, r_.uniform(m + 22, CELL - m - 12) * s
            w = r_.uniform(5, 10) * s
            d.rectangle((px, py, px + w, py + w * 0.55), outline=(160, 132, 88, 255), width=s)
        plate = layer.resize((CELL, CELL), Image.LANCZOS)
        self.canvas.alpha_composite(plate, (x0, y0))

    def check(self):
        for c, r, x0, y0, x1, y1, what in self.boxes:
            assert c * CELL <= x0 and x1 <= (c + 1) * CELL and r * CELL <= y0 and y1 <= (r + 1) * CELL, (what, c, r, x0, y0, x1, y1)
