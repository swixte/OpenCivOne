"""Cuts the v11 sprite sheets into named sprites with the theme's own magenta key (tools/hdart/painted_assets.py)."""
import sys, os
import os
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "../../.."))
sys.path.insert(0, os.path.join(ROOT, 'tools/hdart'))
from PIL import Image
from painted_assets import cut_sprites
NAMES = {'buildings': ['palace', 'temple', 'granary', 'barracks', 'library', 'market'],
         'houses': [f'house{i}' for i in range(16)],
         'walls': ['wall_long', 'wall_short', 'tower', 'gate']}
os.makedirs('sprites', exist_ok=True)
import os.path
for sheet, names in NAMES.items():
    if not os.path.exists(f'sheet-{sheet}.png'):
        continue
    im = Image.open(f'sheet-{sheet}.png')
    sprites = cut_sprites(im, min_area=3000)
    # cut_sprites returns blobs in scan order; keep them with their positions so rows read left to right
    import numpy as np
    from scipy import ndimage
    from painted_assets import key_out
    a = key_out(im)[..., 3] > 127
    labels, _ = ndimage.label(ndimage.binary_dilation(a, iterations=2))
    boxes = [sl for i, sl in enumerate(ndimage.find_objects(labels), 1) if (labels[sl] == i).sum() >= 3000]
    order = sorted(range(len(boxes)), key=lambda k: (round((boxes[k][0].start + boxes[k][0].stop) / 2 / 300), boxes[k][1].start))
    print(sheet, len(sprites), 'blobs for', len(names), 'names')
    for n, k in zip(names, order):
        sprites[k].save(f'sprites/{n}.png'); print(' ', n, sprites[k].size)
