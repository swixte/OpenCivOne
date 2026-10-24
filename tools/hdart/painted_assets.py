"""Prepares art for the painted map (src/Graphics/PaintedMap.cs) from generated images.

Input folder (as written by generate_painted.py):
  tex_NAME.png    ground textures: grass, plains, desert, tundra, snow, ocean, shallow, swamp, floor
  SET.png         sprite sheets on flat magenta: trees, jungle, hills, mountains

Output folder (copy it to the game's hd/painted):
  ground/NAME.png   textures made to repeat without seams, 512x512
  SET/N.png         each sprite cut from its sheet, with the magenta keyed out

Usage: python3 painted_assets.py INPUT_DIR OUTPUT_DIR
"""

import os
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

import style

TEXTURES = ['grass', 'plains', 'desert', 'tundra', 'snow', 'ocean', 'shallow', 'swamp', 'floor']
SETS = ['trees', 'jungle', 'hills', 'mountains']
TEXTURE_SIZE = 512


def cut_sprites(sheet, min_area=400):
    """Sprites from a sheet on flat magenta: alpha from the colour key, magenta spill removed, one per blob."""
    a = np.asarray(sheet.convert('RGB'), dtype=np.float64)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    key = np.minimum(r, b) - g                       # about 255 on the background, low on the art
    alpha = ndimage.grey_erosion(np.clip((150 - key) / 110, 0, 1), size=(3, 3))
    # red and blue may not both exceed green by more than the art itself does
    spill = np.clip(np.minimum(r, b) - g - 4, 0, None)
    a = a.copy()
    a[..., 0] -= spill
    a[..., 2] -= spill
    rgba = np.concatenate([np.clip(a, 0, 255), alpha[..., None] * 255], -1)
    labels, _ = ndimage.label(ndimage.binary_dilation(alpha > 0.5, iterations=2))
    out = []
    for i, sl in enumerate(ndimage.find_objects(labels), 1):
        if (labels[sl] == i).sum() < min_area:
            continue
        spr = rgba[sl].copy()
        spr[..., 3] *= labels[sl] == i
        out.append(Image.fromarray(np.rint(spr).astype(np.uint8), 'RGBA'))
    return out


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


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
