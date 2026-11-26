"""Animated water test: makes the sea and rivers of a still mockup move, as the painted map could in the game.

Water is found by colour (blue at least equal to green and above red), kept as the large sea and long thin rivers
(the compact blue badges drop out), and the white crests inside the sea are added. Each frame displaces the water:
a slow rolling swell on the sea, a short travelling ripple along rivers, and a light shimmer.

Usage: python3 water_test.py MOCKUP.png OUT.gif [SIDEBAR_WIDTH]
"""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

src, out = sys.argv[1], sys.argv[2]
sidebar = int(sys.argv[3]) if len(sys.argv) > 3 else 260
im = np.asarray(Image.open(src).convert('RGB')).astype(np.float32)
H, W = im.shape[:2]
r, g, b = im[..., 0], im[..., 1], im[..., 2]

core = ndimage.binary_closing((b > r + 6) & (b >= g - 4), iterations=2)
lab, n = ndimage.label(core)
keep = []
for i, sl in enumerate(ndimage.find_objects(lab)):
    area = (lab[sl] == i + 1).sum()
    h, w = sl[0].stop - sl[0].start, sl[1].stop - sl[1].start
    if area > 2500 or (area > 300 and max(h, w) > 60 and area / (h * w) < 0.35):
        keep.append(i + 1)
core = np.isin(lab, keep)
core[:, :sidebar] = False
sea = ndimage.binary_opening(core, iterations=6)
sea_zone = ndimage.binary_dilation(ndimage.binary_closing(sea, iterations=12), iterations=3)
water = core | ((r > 160) & (g > 150) & (b > 130) & sea_zone)
river = core & ~ndimage.binary_dilation(sea, iterations=3)
soft = ndimage.gaussian_filter(water.astype(np.float32), 1.2)[..., None]

yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
frames, N = [], 16
for t in range(N):
    ph = 2 * np.pi * t / N
    dx = np.where(river, np.sin((xx + yy) / 5.0 - 2 * ph), 2.0 * np.sin(yy / 23.0 + ph) + 0.7 * np.sin(yy / 7.0 - 2 * ph))
    dy = np.where(river, np.cos((xx - yy) / 5.0 - 2 * ph), 1.5 * np.cos(xx / 31.0 + ph) + 0.5 * np.cos(xx / 9.0 + 2 * ph))
    moved = np.stack([ndimage.map_coordinates(im[..., c], [yy + dy, xx + dx], order=1, mode='nearest') for c in range(3)], -1)
    light = np.where(river, 0.94 + 0.08 * np.sin((xx + yy) / 9.0 - 2 * ph), 0.93 + 0.07 * np.sin(xx / 17.0 + yy / 29.0 - 2 * ph))
    frames.append(Image.fromarray((im * (1 - soft) + np.clip(moved * light[..., None], 0, 255) * soft).astype(np.uint8)))

crop = (sidebar + 120, 200, W, H - 24)
small = [f.crop(crop).resize(((crop[2] - crop[0]) * 2 // 3, (crop[3] - crop[1]) * 2 // 3), Image.LANCZOS) for f in frames]
pal = small[0].quantize(colors=230, method=Image.MEDIANCUT)
q = [f.quantize(palette=pal, dither=Image.NONE) for f in small]
q[0].save(out, save_all=True, append_images=q[1:], duration=110, loop=0, optimize=True)
