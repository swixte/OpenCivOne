"""Paints the resources that sit inside a terrain (gold in mountains, gems in jungle, coal in hills) as soft-edged
patches seen from straight above, so they look woven into the land instead of stamped on it: gold veins and nuggets in
the gullies of ridged rock, a clearing with clusters of coloured crystals, dark coal seams across a hillside.

Usage: python3 resource_patches.py PAINTED_DIR [--seed 1]
  writes PAINTED_DIR/mountains_gold/0.png, 1.png, jungle_gems/0.png, hills_coal/0.png, 1.png
"""

import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ridge_texture import gradient, gullies, periodic_noise  # noqa: E402

W, H = 256, 224


def soft_mask(rng, falloff=0.32):
    """An irregular soft-edged blob filling most of the patch."""
    ys, xs = np.mgrid[0:H, 0:W].astype(np.float32)
    nx, ny = (xs - W / 2) / (W / 2), (ys - H / 2) / (H / 2)
    r = np.sqrt(nx ** 2 + ny ** 2)
    wobble = periodic_noise(rng, 256, 5)[:H, :W] * 0.1
    return np.clip((1 - r + wobble) / falloff, 0, 1)


def light(height, strength):
    gx, gy = gradient(height)
    gx, gy = gx * strength, gy * strength
    nz = 1 / np.sqrt(gx ** 2 + gy ** 2 + 1)
    return np.clip((gx * 0.6 + gy * 0.6 + 0.52) * nz, 0, 1)


def ridged(rng, ranges=2.0):
    base = periodic_noise(rng, 256, ranges) * 0.8 + periodic_noise(rng, 256, ranges * 2) * 0.35
    h = (1 - np.abs(np.tanh(base * 0.55))) ** 1.15
    flank = np.clip(h * (1.1 - h) * 4.5, 0, 1)
    for spacing, freq, amp in ((32, 1.5, 0.2), (16, 1.5, 0.1), (8, 1.4, 0.05)):
        h = h + gullies(h, spacing, freq, 0.8) * amp * (0.35 + 0.65 * flank)
    return (h - h.min()) / (h.max() - h.min())


def gold(rng):
    h = ridged(rng, 3)[:H, :W]
    shade = light(h, 140)
    tone = 0.5 + 0.8 * shade
    rock = np.array([128, 122, 114], np.float32)[None, None] * tone[..., None]
    # gold lies in the gullies and along the seams that cross the slopes
    seam = periodic_noise(rng, 256, 14)[:H, :W]
    vein = np.clip((0.28 - np.abs(seam)) / 0.28, 0, 1) ** 2 * np.clip((0.75 - h) / 0.4, 0, 1)
    gully = np.clip((0.42 - h) / 0.25, 0, 1)
    amount = np.clip(vein * 1.2 + gully * 0.7, 0, 1)
    metal = np.array([226, 168, 52], np.float32)[None, None] * (0.6 + 0.75 * shade)[..., None]
    col = rock * (1 - amount[..., None]) + metal * amount[..., None]
    img = Image.fromarray(col.clip(0, 255).astype(np.uint8))
    d = ImageDraw.Draw(img)
    # nuggets: small bright gold lumps with a highlight on their top left
    for _ in range(60):
        x, y = rng.uniform(30, W - 30), rng.uniform(30, H - 30)
        if amount[int(y), int(x)] < 0.35:
            continue
        r = rng.uniform(2, 4.5)
        d.ellipse([x - r + 1, y - r + 1, x + r + 1, y + r + 1], fill=(70, 50, 20))
        d.ellipse([x - r, y - r, x + r, y + r], fill=(232, 178, 58))
        d.ellipse([x - r * 0.6, y - r * 0.6, x, y], fill=(255, 236, 160))
    return img, soft_mask(rng, 0.3)


def gems(rng):
    # a dark, damp clearing in the jungle with clusters of crystals catching the light
    base = periodic_noise(rng, 256, 10)[:H, :W]
    col = np.array([58, 64, 40], np.float32)[None, None] * (1 + base * 0.12)[..., None]
    img = Image.fromarray(col.clip(0, 255).astype(np.uint8))
    d = ImageDraw.Draw(img)
    hues = [((30, 190, 100), (190, 255, 210)), ((30, 100, 235), (180, 215, 255)), ((220, 30, 70), (255, 170, 190)),
            ((170, 60, 230), (235, 190, 255)), ((245, 190, 40), (255, 245, 190))]
    for _ in range(7):
        cx, cy = rng.uniform(60, W - 60), rng.uniform(55, H - 55)
        dark, bright = hues[rng.integers(len(hues))]
        for _ in range(rng.integers(3, 7)):
            x, y = cx + rng.normal(0, 9), cy + rng.normal(0, 9)
            s = rng.uniform(12, 19)
            a = rng.uniform(0, np.pi)
            pts = [(x + np.cos(a + k * np.pi / 2) * s * (1 if k % 2 == 0 else 0.55),
                    y + np.sin(a + k * np.pi / 2) * s * (1 if k % 2 == 0 else 0.55)) for k in range(4)]
            d.polygon([(px + 2, py + 2) for px, py in pts], fill=(20, 22, 14))
            d.polygon(pts, fill=dark)
            # the facet turned to the light
            d.polygon([pts[0], pts[1], (x, y)], fill=bright)
            d.polygon([pts[3], pts[0], (x, y)], fill=tuple(int((a_ + b_) / 2) for a_, b_ in zip(dark, bright)))
    mask = soft_mask(rng, 0.35) * 0.95
    return img, mask


def coal(rng):
    base = periodic_noise(rng, 256, 4)[:H, :W]
    h = (base - base.min()) / (base.max() - base.min())
    shade = light(h, 60)
    col = np.array([132, 118, 84], np.float32)[None, None] * (0.6 + 0.6 * shade)[..., None]
    seam = periodic_noise(rng, 256, 9)[:H, :W]
    # broad dark seams of coal outcropping across the slope, with soft edges
    band = np.clip((0.75 - np.abs(seam)) / 0.45, 0, 1) ** 1.2
    black = np.array([38, 34, 32], np.float32)[None, None] * (0.8 + 0.5 * shade)[..., None]
    col = col * (1 - band[..., None]) + black * band[..., None]
    return Image.fromarray(col.clip(0, 255).astype(np.uint8)), soft_mask(rng, 0.3)


def save(img, mask, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    a = Image.fromarray((mask * 255).clip(0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1))
    out = img.convert('RGBA')
    out.putalpha(a)
    out.save(path)


def main():
    args = sys.argv[1:]
    seed = 1
    if '--seed' in args:
        i = args.index('--seed'); seed = int(args[i + 1]); del args[i:i + 2]
    if len(args) != 1:
        sys.exit(__doc__)
    folder = args[0]
    for k in range(2):
        save(*gold(np.random.default_rng(seed * 10 + k)), os.path.join(folder, 'mountains_gold', f'{k}.png'))
        save(*coal(np.random.default_rng(seed * 20 + k)), os.path.join(folder, 'hills_coal', f'{k}.png'))
    save(*gems(np.random.default_rng(seed * 30)), os.path.join(folder, 'jungle_gems', '0.png'))
    print('resource patches written to', folder)


if __name__ == '__main__':
    main()
