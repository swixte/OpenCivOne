"""Style packs: build the tile set from art images instead of drawing it in code.

A style pack is a folder of images. Every image is optional; whatever is missing is drawn by the
procedural generator as before.

Terrain textures (any size; a square from the middle is used, see `scale` below):
    grass.png         ground under every land tile
    grassland.png  plains.png  desert.png  tundra.png  arctic.png  swamp.png
    forest_floor.png  jungle_floor.png  hills_floor.png  rock.png (mountain ground)
    ocean.png         open water; shallows are lightened toward the coast
    sand.png          beaches
Feature sprites (on a transparent or plain background, which is removed):
    tree.png  jungle_tree.png  hill.png  mountain.png  city.png  hut.png
Unit sprites, named after the unit: settlers.png, militia.png, ... (see units.UNIT_NAMES)

pack.json (optional) sets how much of each texture image becomes one tile, for example
    {"scale": {"grassland": 0.25, "ocean": 0.4}}
where 0.25 means a tile shows a square a quarter as wide as the image. The default is 0.3.
"""

import json
import os
import numpy as np
from PIL import Image

PACK = None


class Pack:
    def __init__(self, folder):
        self.folder = folder
        self.files = {os.path.splitext(f)[0].lower(): os.path.join(folder, f)
                      for f in os.listdir(folder) if f.lower().endswith(('.png', '.jpg', '.jpeg', '.webp'))}
        manifest = os.path.join(folder, 'pack.json')
        self.config = json.load(open(manifest)) if os.path.exists(manifest) else {}
        self.cache = {}

    def has(self, name):
        return name in self.files

    def image(self, name):
        if name not in self.cache:
            self.cache[name] = Image.open(self.files[name]).convert('RGBA')
        return self.cache[name]


def load(folder):
    global PACK
    PACK = Pack(folder) if folder else None
    return PACK


def has(name):
    return PACK is not None and PACK.has(name)


# ---------- textures ----------

def seamless(img, size):
    """A square tile that repeats without seams, from the middle of an image.

    The tile is a blend of the crop and the same crop shifted by half its size: the shifted copy
    repeats seamlessly at the edges, and the unshifted one covers the shifted copy's seams in the middle.
    """
    a = np.asarray(img.resize((size, size), Image.LANCZOS).convert('RGBA'), dtype=np.float64)
    b = np.roll(np.roll(a, size // 2, 0), size // 2, 1)
    y, x = np.mgrid[0:size, 0:size] + 0.5
    # weight: 1 in the middle, 0 at the edges
    w = np.minimum(np.minimum(x, size - x), np.minimum(y, size - y)) / (size / 2)
    w = np.clip(w * 1.6 - 0.3, 0, 1)[..., None]
    w = w * w * (3 - 2 * w)
    out = a * w + b * (1 - w)
    out[..., 3] = 255
    return np.rint(out).astype(np.uint8)


def texture(name, size):
    """The pack's texture for `name` as a seamless size x size tile, or None."""
    if not has(name):
        return None
    key = ('texture', name, size)
    if key not in PACK.cache:
        img = PACK.image(name)
        scale = PACK.config.get('scale', {}).get(name, 0.3)
        side = max(8, int(min(img.size) * scale))
        # take the crop at twice the tile size first, then let seamless() shrink it smoothly
        left = (img.size[0] - side) // 2
        top = (img.size[1] - side) // 2
        crop = img.crop((left, top, left + side, top + side))
        PACK.cache[key] = seamless(crop, size)
    return PACK.cache[key]


# ---------- sprites ----------

def remove_background(img, tolerance=40):
    """Make the background transparent: the color at the corners, flood-filled from the edges."""
    a = np.asarray(img.convert('RGBA')).copy()
    if (a[..., 3] < 250).mean() > 0.05:
        return a      # already has transparency
    h, w = a.shape[:2]
    rgb = a[..., :3].astype(np.int64)
    corners = np.array([rgb[0, 0], rgb[0, -1], rgb[-1, 0], rgb[-1, -1]])
    bg = np.median(corners, axis=0)
    close = (np.abs(rgb - bg).sum(-1) <= tolerance)
    # flood fill from the border through background-colored pixels
    seen = np.zeros((h, w), dtype=bool)
    stack = [(y, x) for y in (0, h - 1) for x in range(w)] + [(y, x) for x in (0, w - 1) for y in range(h)]
    while stack:
        y, x = stack.pop()
        if 0 <= y < h and 0 <= x < w and not seen[y, x] and close[y, x]:
            seen[y, x] = True
            stack.extend(((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)))
    a[seen, 3] = 0
    return a


def sprite(name):
    """The pack's sprite for `name`, background removed and trimmed to its visible pixels, or None."""
    if not has(name):
        return None
    key = ('sprite', name)
    if key not in PACK.cache:
        a = remove_background(PACK.image(name))
        ys, xs = np.nonzero(a[..., 3] > 16)
        if len(ys) == 0:
            PACK.cache[key] = None
        else:
            PACK.cache[key] = a[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    return PACK.cache[key]


def fit(spr, width=None, height=None):
    """Resize a sprite to a width or height (keeping its shape), smoothly."""
    h, w = spr.shape[:2]
    if width is not None and height is not None:
        s = min(width / w, height / h)
    elif width is not None:
        s = width / w
    else:
        s = height / h
    nw, nh = max(1, int(round(w * s))), max(1, int(round(h * s)))
    return np.asarray(Image.fromarray(spr).resize((nw, nh), Image.LANCZOS))


def paste(dst, spr, cx, bottom):
    """Alpha-composite a sprite onto dst with its bottom centre at (cx, bottom), clipped to dst."""
    h, w = spr.shape[:2]
    x0, y0 = int(round(cx - w / 2)), int(round(bottom - h))
    sx0, sy0 = max(0, -x0), max(0, -y0)
    x0c, y0c = max(0, x0), max(0, y0)
    x1, y1 = min(dst.shape[1], x0 + w), min(dst.shape[0], y0 + h)
    if x1 <= x0c or y1 <= y0c:
        return
    src = spr[sy0:sy0 + (y1 - y0c), sx0:sx0 + (x1 - x0c)].astype(np.float64)
    region = dst[y0c:y1, x0c:x1].astype(np.float64)
    sa = src[..., 3:4] / 255
    da = region[..., 3:4] / 255
    oa = sa + da * (1 - sa)
    rgb = np.where(oa > 0, (src[..., :3] * sa + region[..., :3] * da * (1 - sa)) / np.maximum(oa, 1e-9), 0)
    dst[y0c:y1, x0c:x1] = np.rint(np.concatenate([rgb, oa * 255], -1)).astype(np.uint8)


def shadow(dst, cx, bottom, rx, ry=None, alpha=80):
    """A soft drop shadow ellipse under a sprite, light coming from the top left."""
    ry = ry if ry is not None else max(1.5, rx * 0.25)
    y, x = np.mgrid[0:dst.shape[0], 0:dst.shape[1]] + 0.5
    d = ((x - cx - rx * 0.15) / rx) ** 2 + ((y - bottom) / ry) ** 2
    m = d <= 1
    a = (alpha * np.clip(1.2 - d, 0, 1)).astype(np.float64)
    region = dst.astype(np.float64)
    keep = m & (a > 0)
    region[keep, :3] = region[keep, :3] * (1 - a[keep, None] / 255)
    region[keep, 3] = np.maximum(region[keep, 3], a[keep])
    dst[...] = np.rint(region).astype(np.uint8)
