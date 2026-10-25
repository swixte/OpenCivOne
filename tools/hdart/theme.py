"""Builds an art theme for the game: a complete HD folder made from one concept image and a style description.

A theme is a folder with a theme.json:

    {
      "name": "dark-painterly",
      "concept": "concept.png",
      "style": "dark, moody, painterly, muted fantasy strategy map, light from the top left",
      "quality": "medium",
      "pictures": {"BIRTH0": "optional subject that replaces the one in pictures.py"}
    }

`build` generates into THEME/hd, at 4x:
  - the pictures listed in pictures.py, as whole-image HD files (NAME.png, 1280x800): backdrops are generated whole
    (with any placed objects put into their rectangles), grid sheets piece by piece into their cells;
  - with --map, the painted map, units and interface as well (generate_painted.py, painted_assets.py, painted_set.py).
Pictures already built are kept, so an interrupted build can be resumed; --force builds them again. Generated pieces are
kept in THEME/pieces and reused by --force: delete a piece to generate it again.

`install` puts a theme into the game's themes/NAME folder: the shared 4x set (assets/hd4 and assets/painted) with the
theme's own files from THEME/hd over it, so a theme only holds what it changes. Then set "Theme": "NAME" in
~/Games/OpenCivOne/Preferences.json.

Usage:
  OPENAI_API_KEY=... python3 theme.py build THEME_DIR [--map] [--only NAME ...] [--force]
  python3 theme.py install THEME_DIR [GAME_DIR]      (GAME_DIR defaults to ~/Dos/Civ1)
  python3 theme.py list
"""

import base64
import io
import json
import os
import shutil
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor

import numpy as np
import requests
from PIL import Image

import painted_assets
import pictures
import style

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, '..', '..', 'assets')
S = 4                       # HD pixels per original pixel
W, H = 320 * S, 200 * S


def call(key, concept, prompt, size, quality):
    """One image from the OpenAI Images API, with the concept image as style reference."""
    with open(concept, 'rb') as f:
        r = requests.post('https://api.openai.com/v1/images/edits', headers={'Authorization': f'Bearer {key}'},
                          data={'model': 'gpt-image-2', 'prompt': prompt, 'size': size, 'quality': quality, 'n': '1'},
                          files={'image[]': (os.path.basename(concept), f, 'image/png')}, timeout=900)
    if r.status_code != 200:
        raise RuntimeError(f'HTTP {r.status_code} {r.text[:300]}')
    return Image.open(io.BytesIO(base64.b64decode(r.json()['data'][0]['b64_json'])))


def backdrop(theme, key, name, subject, calm):
    prompt = (f'Use the reference image only for its art style and colours: {theme["style"]}. '
              f'A wide full-screen game illustration: {subject} {calm} No text, letters, numbers, frame or border.')
    img = call(key, theme['concept'], prompt, '1536x1024', theme.get('quality', 'medium')).convert('RGB')
    # the 3:2 image is cropped to the game's 16:10 screen
    return img.crop((0, 32, 1536, 992)).resize((W, H), Image.LANCZOS)


def piece(theme, key, subject, framing, cache):
    """One object generated alone on magenta and cut out. Kept in the theme's pieces folder, so a picture can be
    composed again without new images; delete a piece to generate it again."""
    if os.path.exists(cache):
        return Image.open(cache).convert('RGBA')
    prompt = (f'Use the reference image only for its art style and colours: {theme["style"]}. '
              f'A single small game {framing}: {subject}, centred and filling most of the image, with a bold, readable '
              'silhouette that stays clear when shrunk to a small icon. It is isolated on a perfectly flat, uniform pure '
              'magenta (#FF00FF) background. No ground, no shadow, no text.')
    obj = painted_assets.cut_object(call(key, theme['concept'], prompt, '1024x1024', theme.get('quality', 'medium')))
    if obj is not None:
        os.makedirs(os.path.dirname(cache), exist_ok=True)
        obj.save(cache)
    return obj


def cache(theme, name, i):
    return os.path.join(theme['dir'], 'pieces', f'{name}_{i:02}.png')


def place(sheet, obj, rect, standing, shade=False):
    """Fits a cut-out object into a rectangle (original pixels) of the sheet; standing ones rest on its bottom.
    With shade, a soft shadow grounds it on the picture below."""
    if obj is None:
        return
    x, y, w, h = (v * S for v in rect)
    fitted = Image.fromarray(style.fit(np.asarray(obj), width=w, height=h - (2 * S if shade else 0)))
    px = x + (w - fitted.width) // 2
    py = y + (h - fitted.height) if standing else y + (h - fitted.height) // 2
    if shade:
        py -= 2 * S
        a = np.array(sheet)
        style.shadow(a, px + fitted.width / 2, py + fitted.height - S, fitted.width * 0.55, fitted.height * 0.12, 110)
        sheet.paste(Image.fromarray(a))
    sheet.alpha_composite(fitted, (px, py))


def grid(theme, key, name, spec, subjects):
    """A sheet of equal cells, one object per cell."""
    sheet = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    cw, ch = spec['cell']
    ox, oy = spec['origin']
    sx, sy = spec['step']
    cols = spec['columns']
    framing = 'figure, full body, seen from the front' if spec.get('standing') else 'object'
    with ThreadPoolExecutor(4) as pool:
        cut = list(pool.map(lambda i: piece(theme, key, subjects[i], framing, cache(theme, name, i)), range(len(subjects))))
    for i, obj in enumerate(cut):
        place(sheet, obj, (ox + (i % cols) * sx, oy + (i // cols) * sy, cw, ch), spec.get('standing'))
    return sheet


def placed(theme, key, name, img, pieces):
    """Objects put into fixed rectangles over a backdrop."""
    sheet = img.convert('RGBA')
    with ThreadPoolExecutor(4) as pool:
        cut = list(pool.map(lambda i: piece(theme, key, pieces[i][1], 'object, seen from a slight angle',
                                            cache(theme, name, i)), range(len(pieces))))
    for (rect, _), obj in zip(pieces, cut):
        place(sheet, obj, rect, True, shade=True)
    return sheet.convert('RGB')


def build(theme_dir, only=None, force=False, with_map=False):
    key = os.environ.get('OPENAI_API_KEY')
    if not key:
        sys.exit('Set OPENAI_API_KEY first.')
    theme = json.load(open(os.path.join(theme_dir, 'theme.json')))
    theme['concept'] = os.path.join(theme_dir, theme['concept'])
    theme['dir'] = theme_dir
    out = os.path.join(theme_dir, 'hd')
    os.makedirs(out, exist_ok=True)
    overrides = theme.get('pictures', {})

    if with_map:
        raw = os.path.join(theme_dir, 'raw')
        env = dict(os.environ, PAINTED_STYLE=theme['style'])
        for cmd in (['generate_painted.py', theme['concept'], raw], ['painted_assets.py', raw, os.path.join(out, 'painted')],
                    ['painted_set.py', out, os.path.join(out, 'painted', 'objects')]):
            subprocess.run([sys.executable, os.path.join(HERE, cmd[0])] + cmd[1:], check=True, env=env)

    jobs = []
    for name, (subject, calm) in pictures.BACKDROPS.items():
        def make(n=name, s=subject, c=calm):
            img = backdrop(theme, key, n, overrides.get(n, s), c)
            return placed(theme, key, n, img, pictures.PLACED[n]) if n in pictures.PLACED else img
        jobs.append((name, make))
    for name, spec in pictures.GRIDS.items():
        subjects = overrides.get(name, spec['items'])
        jobs.append((name, lambda n=name, sp=spec, su=subjects: grid(theme, key, n, sp, su)))

    for name, make in jobs:
        if name in pictures.NEVER_PAINT or (only and name not in only):
            continue
        path = os.path.join(out, name + '.png')
        if os.path.exists(path) and not force:
            print(f'{name}: kept')
            continue
        try:
            make().save(path)
            print(f'{name}: written')
        except Exception as e:      # one failed picture should not stop the rest
            print(f'{name}: failed, {e}')


def install(theme_dir, game_dir):
    name = json.load(open(os.path.join(theme_dir, 'theme.json')))['name']
    out = os.path.join(game_dir, 'themes', name)
    shutil.rmtree(out, ignore_errors=True)
    shutil.copytree(os.path.join(ASSETS, 'hd4'), out)
    shutil.copytree(os.path.join(ASSETS, 'painted'), os.path.join(out, 'painted'))
    own = os.path.join(theme_dir, 'hd')
    if os.path.isdir(own):
        shutil.copytree(own, out, dirs_exist_ok=True)
    print(f'Installed in {out}; set "Theme": "{name}" in ~/Games/OpenCivOne/Preferences.json to use it.')


def main():
    args = sys.argv[1:]
    if not args or args[0] not in ('build', 'install', 'list'):
        sys.exit(__doc__)
    if args[0] == 'list':
        for name, (subject, _) in pictures.BACKDROPS.items():
            extra = f' + {len(pictures.PLACED[name])} placed' if name in pictures.PLACED else ''
            print(f'{name:10} backdrop{extra}  {subject}')
        for name, spec in pictures.GRIDS.items():
            print(f'{name:10} grid      {len(spec["items"])} pieces of {spec["cell"][0]}x{spec["cell"][1]}')
        return
    if args[0] == 'install':
        if len(args) < 2:
            sys.exit(__doc__)
        install(args[1], args[2] if len(args) > 2 else os.path.expanduser('~/Dos/Civ1'))
        return
    with_map = '--map' in args
    force = '--force' in args
    only = None
    rest = [a for a in args[1:] if a not in ('--map', '--force')]
    if '--only' in rest:
        i = rest.index('--only')
        only = set(n.upper() for n in rest[i + 1:])
        rest = rest[:i]
    build(rest[0], only, force, with_map)


if __name__ == '__main__':
    main()
