"""Repaints the game's full-screen pictures in the style of a concept image, as HD whole-image files.

The pictures come from your own copy of the game: set "ExportHDTemplates": true in the Debug section of
~/Games/OpenCivOne/Preferences.json and play for a while, and every picture the game loads is written as a template at the HD scale
to ~/Games/OpenCivOne/hd-templates. This tool sends each full-screen picture to the OpenAI Images API together with the
concept image, asking for the same picture painted in the concept's style with the composition kept, and writes it as
OUT/NAME.png at the HD size (1280x800 at 4x), which the game shows in place of the original.

Sprite sheets, which the game cuts into pieces at fixed places (map tiles, units, city buildings, leader faces, wonders
and the like), are skipped: a repainted sheet would not line up with its pieces. Check each result in the game; delete a
picture's file to go back to the original.

Usage: OPENAI_API_KEY=... python3 repaint_pictures.py TEMPLATES_DIR CONCEPT.png OUT_DIR [--scale 4] [--only NAME ...]
"""

import base64
import io
import os
import sys
from concurrent.futures import ThreadPoolExecutor

import requests
from PIL import Image

# PIC files the game uses as sprite sheets, by name prefix
SHEETS = ('SP257', 'SP299', 'TER257', 'CITYPIX', 'WONDERS', 'SETTLERS', 'POP', 'CASTLE', 'SPACEST', 'ICONPG', 'KING',
          'SLAM', 'TORCH', 'DOCKER', 'INVADER2', 'INVADER3', 'NUKE')

PROMPT = ('Repaint the first image in the art style of the second image: dark, moody, painterly, high detail, light from '
          'the top left. Keep the first image\'s composition exactly: every shape, figure, building and area stays in the '
          'same place with the same size and the same meaning, and large flat areas stay calm so text can be written on '
          'them. Lettering that is in the picture stays, redrawn in the same place and size. Keep the black bars at the top and '
          'bottom black. Do not add any new text, letters or numbers.')

SIZE = (1536, 1024)


def is_picture(name, size):
    # templates are written at the game's HD scale: 640x400 at 2x, 1280x800 at 4x
    return size[0] * 200 == size[1] * 320 and size[0] % 320 == 0 and not name.upper().startswith(SHEETS)


def prepare(template):
    """The template on black, resized to the API's 1536 width and centred in a 1536x1024 canvas."""
    img = Image.open(template).convert('RGBA')
    flat = Image.new('RGB', img.size, (0, 0, 0))
    flat.paste(img, (0, 0), img)
    big = flat.resize((1536, 960), Image.LANCZOS)
    canvas = Image.new('RGB', SIZE, (0, 0, 0))
    canvas.paste(big, (0, 32))
    buf = io.BytesIO()
    canvas.save(buf, 'PNG')
    return buf.getvalue()


def repaint(key, concept, template, out_path, scale):
    with open(concept, 'rb') as c:
        r = requests.post('https://api.openai.com/v1/images/edits', headers={'Authorization': f'Bearer {key}'},
                          data={'model': 'gpt-image-2', 'prompt': PROMPT, 'size': f'{SIZE[0]}x{SIZE[1]}', 'quality': 'high', 'n': '1'},
                          files=[('image[]', ('picture.png', prepare(template), 'image/png')),
                                 ('image[]', (os.path.basename(concept), c, 'image/png'))], timeout=900)
    if r.status_code != 200:
        return f'{os.path.basename(template)}: HTTP {r.status_code} {r.text[:200]}'
    result = Image.open(io.BytesIO(base64.b64decode(r.json()['data'][0]['b64_json']))).convert('RGB')
    result = result.crop((0, 32, 1536, 992)).resize((320 * scale, 200 * scale), Image.LANCZOS)
    result.save(out_path)
    return f'{os.path.basename(template)}: written {out_path}'


def main():
    args = sys.argv[1:]
    scale, only = 4, None
    if '--scale' in args:
        i = args.index('--scale')
        scale = int(args[i + 1])
        del args[i:i + 2]
    if '--only' in args:
        i = args.index('--only')
        only = [n.upper() for n in args[i + 1:]]
        del args[i:]
    key = os.environ.get('OPENAI_API_KEY')
    if not key or len(args) != 3:
        sys.exit(__doc__)
    templates, concept, out = args
    os.makedirs(out, exist_ok=True)
    jobs = []
    for f in sorted(os.listdir(templates)):
        name, ext = os.path.splitext(f)
        if ext.lower() != '.png' or (only and name.upper() not in only):
            continue
        path = os.path.join(templates, f)
        with Image.open(path) as img:
            size = img.size
        if only or is_picture(name, size):
            jobs.append((path, os.path.join(out, name.upper() + '.png')))
        else:
            print(f'{f}: skipped (sprite sheet or not a full-screen picture)')
    with ThreadPoolExecutor(4) as pool:
        for line in pool.map(lambda j: repaint(key, concept, j[0], j[1], scale), jobs):
            print(line)


if __name__ == '__main__':
    main()
