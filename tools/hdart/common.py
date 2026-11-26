"""Shared helpers for the HD art generator: tileable noise, palettes, dithering and sprite drawing.

All art is drawn at 2x (a 16x16 original tile is 32x32 here), lit from the top left.
Textures repeat every 32 pixels, so tiles of the same kind join without seams.
"""

import numpy as np
from PIL import Image

T = 32          # HD tile size
SCALE = 2       # HD pixels per original pixel

# ---------- noise ----------

def _hash(ix, iy, seed):
    h = (ix.astype(np.uint64) * np.uint64(374761393) + iy.astype(np.uint64) * np.uint64(668265263)
         + np.uint64(seed) * np.uint64(1013904223)) & np.uint64(0xffffffff)
    h = ((h ^ (h >> np.uint64(15))) * np.uint64(2246822519)) & np.uint64(0xffffffff)
    h = ((h ^ (h >> np.uint64(13))) * np.uint64(3266489917)) & np.uint64(0xffffffff)
    h = h ^ (h >> np.uint64(16))
    return h.astype(np.float64) / 4294967296.0


def hash2(x, y, seed):
    """Random value in [0, 1) for integer coordinates (scalars or arrays)."""
    return _hash(np.asarray(x, dtype=np.int64) & 0xffffffff, np.asarray(y, dtype=np.int64) & 0xffffffff, seed)


def vnoise(x, y, cell, seed, period=T):
    """Value noise with lattice spacing `cell` pixels, repeating every `period` pixels."""
    p = max(1, period // cell)
    gx, gy = x / cell, y / cell
    ix, iy = np.floor(gx).astype(np.int64), np.floor(gy).astype(np.int64)
    fx, fy = gx - ix, gy - iy
    u, v = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    a = _hash(ix % p, iy % p, seed)
    b = _hash((ix + 1) % p, iy % p, seed)
    c = _hash(ix % p, (iy + 1) % p, seed)
    d = _hash((ix + 1) % p, (iy + 1) % p, seed)
    return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v


def fbm(x, y, seed, cells=(16, 8, 4), period=T):
    total, amp, norm = 0.0, 0.5, 0.0
    for i, c in enumerate(cells):
        total = total + amp * vnoise(x, y, c, seed + i * 17, period)
        norm += amp
        amp *= 0.5
    return total / norm


def grid(w=T, h=T):
    y, x = np.mgrid[0:h, 0:w].astype(np.float64)
    return x, y


def light(field_fn, x, y, k=1.0):
    """Lambert-like term for a height field, light from the top left: positive on slopes facing it."""
    return (field_fn(x + 1, y + 1) - field_fn(x - 1, y - 1)) * k


# ---------- colors ----------

def hexc(s, a=255):
    s = s.lstrip('#')
    return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16), a)


def ramp(*colors):
    return np.array([hexc(c) for c in colors], dtype=np.uint8)


BAYER = (np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], dtype=np.float64) + 0.5) / 16 - 0.5


def bayer(x, y):
    return BAYER[(np.asarray(y, dtype=np.int64) & 3), (np.asarray(x, dtype=np.int64) & 3)]


def pick(r, v, x, y, dither=0.9):
    """Color from a dark-to-light ramp for brightness v in [0, 1], ordered-dithered between steps."""
    i = np.rint(np.asarray(v) * (len(r) - 1) + bayer(x, y) * dither).astype(np.int64)
    return r[np.clip(i, 0, len(r) - 1)]


# Key colors: the engine replaces them with player colors (see docs/hd-art.md).
def key_primary(shade=0x80):
    s = int(max(1, min(255, shade)))
    return (s, 0, s, 255)


def key_secondary(shade=0x80):
    s = int(max(1, min(255, shade)))
    return (0, s, s, 255)


def is_key(px):
    r, g, b, a = int(px[0]), int(px[1]), int(px[2]), int(px[3])
    return a == 255 and ((g == 0 and r == b and r > 0) or (r == 0 and g == b and g > 0))


def avoid_keys(img):
    """Nudge accidental key colors in ordinary art, so only intended pixels follow player colors."""
    r, g, b, a = img[..., 0], img[..., 1], img[..., 2], img[..., 3]
    accidental = (a == 255) & (((g == 0) & (r == b) & (r > 0)) | ((r == 0) & (g == b) & (g > 0)))
    img[accidental & (g == 0), 1] = 1
    img[accidental & (r == 0), 0] = 1
    return img


# ---------- images ----------

def blank(w=T, h=T):
    return np.zeros((h, w, 4), dtype=np.uint8)


def over(dst, src):
    """Alpha-composite src over dst (both RGBA uint8, same size); key colors are copied as they are."""
    sa = src[..., 3:4].astype(np.float64) / 255
    da = dst[..., 3:4].astype(np.float64) / 255
    oa = sa + da * (1 - sa)
    rgb = np.where(oa > 0, (src[..., :3] * sa + dst[..., :3] * da * (1 - sa)) / np.maximum(oa, 1e-9), 0)
    out = np.concatenate([rgb, oa * 255], axis=-1)
    out = np.rint(out).astype(np.uint8)
    full = src[..., 3] == 255
    out[full] = src[full]
    return out


def save(img, path):
    Image.fromarray(np.ascontiguousarray(img), 'RGBA').save(path)


# ---------- sprite drawing (units, buildings, icons) ----------

class Sprite:
    """Draws flat shapes as materials, then shades and outlines them in a pixel art style.

    Each pixel holds a material name and a tone offset; rendering looks the color up in the
    material's ramp, adds rim light on top-left edges and shade on bottom-right edges, and
    draws a dark outline around the silhouette.
    """

    def __init__(self, w, h, materials):
        self.w, self.h = w, h
        self.mat = np.full((h, w), '', dtype=object)
        self.tone = np.zeros((h, w), dtype=np.float64)
        self.materials = materials      # name -> ramp (dark to light) or 'P'/'S' for key colors
        self.y, self.x = np.mgrid[0:h, 0:w].astype(np.float64) + 0.5

    def fill(self, mask, mat, tone=0.0):
        self.mat[mask] = mat
        self.tone[mask] = tone

    def rect(self, x0, y0, x1, y1, mat, tone=0.0):
        self.fill((self.x >= x0) & (self.x < x1) & (self.y >= y0) & (self.y < y1), mat, tone)

    def ellipse(self, cx, cy, rx, ry, mat, tone=0.0, shade=0.0):
        d = ((self.x - cx) / rx) ** 2 + ((self.y - cy) / ry) ** 2
        m = d <= 1
        # optional spherical shading: lighter toward the top left
        t = tone + shade * (-(self.x - cx) / rx - (self.y - cy) / ry) * 0.5
        self.mat[m] = mat
        self.tone[m] = t[m]

    def poly(self, pts, mat, tone=0.0):
        self.fill(poly_mask(pts, self.x, self.y), mat, tone)

    def line(self, x0, y0, x1, y1, width, mat, tone=0.0):
        px, py = self.x - x0, self.y - y0
        dx, dy = x1 - x0, y1 - y0
        ll = dx * dx + dy * dy
        t = np.clip((px * dx + py * dy) / ll, 0, 1) if ll else 0
        d = np.hypot(px - t * dx, py - t * dy)
        self.fill(d <= width / 2, mat, tone)

    def render(self, outline=(24, 18, 14, 255), auto_light=True, shadow=None):
        img = blank(self.w, self.h)
        filled = self.mat != ''
        for name, r in self.materials.items():
            m = self.mat == name
            if not m.any():
                continue
            t = self.tone.copy()
            if auto_light:
                same = lambda dx, dy: np.roll(np.roll(self.mat == name, -dy, 0), -dx, 1)
                lit = ~same(-1, 0) | ~same(0, -1)
                dark = ~same(1, 0) | ~same(0, 1)
                t = t + np.where(lit & ~dark, 0.6, 0) - np.where(dark & ~lit, 0.6, 0)
            if isinstance(r, str):
                base = 0x80 + t * 0x20
                for yy, xx in zip(*np.nonzero(m)):
                    s = int(np.clip(base[yy, xx], 0x40, 0xd0))
                    img[yy, xx] = key_primary(s) if r == 'P' else key_secondary(s)
            else:
                n = len(r)
                idx = np.clip(np.rint((n - 1) / 2 + t), 0, n - 1).astype(np.int64)
                img[m] = r[idx[m]]
        if outline is not None:
            o = np.zeros_like(filled)
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                o |= np.roll(np.roll(filled, dy, 0), dx, 1)
            o &= ~filled
            # do not wrap around the sprite edges
            o[0, :] &= filled[1, :]
            o[-1, :] &= filled[-2, :]
            o[:, 0] &= filled[:, 1]
            o[:, -1] &= filled[:, -2]
            img[o] = outline
        if shadow is not None:
            cx, cy, rx, ry, alpha = shadow
            d = ((self.x - cx) / rx) ** 2 + ((self.y - cy) / ry) ** 2
            sm = (d <= 1) & (img[..., 3] == 0)
            img[sm] = (0, 0, 0, alpha)
        return img


def poly_mask(pts, x, y):
    """Even-odd fill of a polygon given as a list of (x, y) points."""
    inside = np.zeros(x.shape, dtype=bool)
    n = len(pts)
    for i in range(n):
        x0, y0 = pts[i]
        x1, y1 = pts[(i + 1) % n]
        cond = ((y0 > y) != (y1 > y))
        with np.errstate(divide='ignore', invalid='ignore'):
            xi = x0 + (y - y0) * (x1 - x0) / (y1 - y0)
        inside ^= cond & (x < xi)
    return inside
