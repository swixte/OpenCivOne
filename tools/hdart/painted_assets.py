"""Prepares art for the painted map (src/Graphics/PaintedMap.cs) from generated images.

Input folder (as written by generate_painted.py):
  tex_NAME.png    ground textures: grass, plains, desert, tundra, snow, ocean, shallow, swamp, floor, and optionally
                  forest, hills, mountains, jungle (a terrain's own ground)
  SET.png         sprite sheets on flat magenta: trees, jungle (jungle_green.png wins), hills, mountains
  unit_*.png, res_*.png, hut.png, fortress.png, town.png, walls.png
                  single objects on flat magenta, for the 4x sprite set (painted_set.py)
  ui_grain.png, ui_stone.png, ui_slate.png
                  interface textures, for the 4x sprite set

Output folder (copy it to the game's hd/painted):
  ground/NAME.png   textures made to repeat without seams, 512x512
  SET/N.png         each sprite cut from its sheet, with the magenta keyed out
  objects/NAME.png  each single object, keyed out and trimmed
  ui/NAME.png       interface textures made to repeat without seams, 1024x1024

Usage: python3 painted_assets.py INPUT_DIR OUTPUT_DIR
"""

import os
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

import style

# forest, hills, mountains and jungle are optional: with them each of those terrains has its own ground
TEXTURES = ['grass', 'plains', 'desert', 'tundra', 'snow', 'ocean', 'shallow', 'swamp', 'floor', 'forest', 'hills', 'mountains', 'jungle']
SETS = ['trees', 'jungle', 'hills', 'mountains']
TEXTURE_SIZE = 512


def key_out(image):
    """RGBA float array: alpha from the magenta colour key, magenta spill removed."""
    a = np.asarray(image.convert('RGB'), dtype=np.float64)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    key = np.minimum(r, b) - g                       # about 255 on the background, low on the art
    alpha = ndimage.grey_erosion(np.clip((150 - key) / 110, 0, 1), size=(3, 3))
    # red and blue may not both exceed green by more than the art itself does
    spill = np.clip(np.minimum(r, b) - g - 4, 0, None)
    a = a.copy()
    a[..., 0] -= spill
    a[..., 2] -= spill
    return np.concatenate([np.clip(a, 0, 255), alpha[..., None] * 255], -1)


def cut_sprites(sheet, min_area=400):
    """Sprites from a sheet on flat magenta, one per blob."""
    rgba = key_out(sheet)
    alpha = rgba[..., 3] / 255
    labels, _ = ndimage.label(ndimage.binary_dilation(alpha > 0.5, iterations=2))
    out = []
    for i, sl in enumerate(ndimage.find_objects(labels), 1):
        if (labels[sl] == i).sum() < min_area:
            continue
        spr = rgba[sl].copy()
        spr[..., 3] *= labels[sl] == i
        out.append(Image.fromarray(np.rint(spr).astype(np.uint8), 'RGBA'))
    return out


def cut_object(image, size=256):
    """A single object on flat magenta: its parts (blobs at least 2% of the largest), trimmed, at most `size` pixels."""
    rgba = key_out(image)
    labels, n = ndimage.label(rgba[..., 3] > 128)
    if n == 0:
        return None
    areas = ndimage.sum(np.ones(labels.shape), labels, range(1, n + 1))
    keep = np.isin(labels, [i + 1 for i, area in enumerate(areas) if area >= areas.max() * 0.02])
    keep = ndimage.binary_dilation(keep, iterations=3)
    rgba[..., 3] *= keep
    ys, xs = np.nonzero(rgba[..., 3] > 8)
    img = Image.fromarray(np.rint(rgba[ys.min():ys.max() + 1, xs.min():xs.max() + 1]).astype(np.uint8), 'RGBA')
    k = size / max(img.size)
    if k < 1:
        img = img.resize((max(1, round(img.width * k)), max(1, round(img.height * k))), Image.LANCZOS)
    return img


def main(src, out):
    os.makedirs(os.path.join(out, 'ground'), exist_ok=True)
    for name in TEXTURES:
        path = os.path.join(src, f'tex_{name}.png')
        if os.path.exists(path):
            img = Image.open(path).convert('RGB')
            tile = style.seamless(img, img.width)
            Image.fromarray(tile).convert('RGB').resize((TEXTURE_SIZE, TEXTURE_SIZE), Image.LANCZOS).save(os.path.join(out, 'ground', f'{name}.png'))
    for name in SETS:
        path = os.path.join(src, f'{name}.png')
        if name == 'jungle' and os.path.exists(os.path.join(src, 'jungle_green.png')):
            path = os.path.join(src, 'jungle_green.png')
        if os.path.exists(path):
            folder = os.path.join(out, name)
            os.makedirs(folder, exist_ok=True)
            for f in os.listdir(folder):
                os.remove(os.path.join(folder, f))
            sprites = cut_sprites(Image.open(path))
            for i, spr in enumerate(sprites):
                # the game draws them at most about 100 pixels tall
                if spr.height > 160:
                    spr = spr.resize((max(1, spr.width * 160 // spr.height), 160), Image.LANCZOS)
                spr.save(os.path.join(folder, f'{i}.png'))
            print(name, len(sprites), 'sprites')
    for name in ('grain', 'stone', 'slate'):
        path = os.path.join(src, f'ui_{name}.png')
        if os.path.exists(path):
            os.makedirs(os.path.join(out, 'ui'), exist_ok=True)
            img = Image.open(path).convert('RGB')
            Image.fromarray(style.seamless(img, img.width)).convert('RGB').save(os.path.join(out, 'ui', f'{name}.png'))
    objects = sorted(f[:-4] for f in os.listdir(src)
                     if f.endswith('.png') and (f.startswith(('unit_', 'res_')) or f[:-4] in ('hut', 'fortress', 'town', 'walls')))
    if objects:
        os.makedirs(os.path.join(out, 'objects'), exist_ok=True)
        for name in objects:
            img = cut_object(Image.open(os.path.join(src, name + '.png')))
            if img is not None:
                img.save(os.path.join(out, 'objects', name + '.png'))
        print(len(objects), 'objects')


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
