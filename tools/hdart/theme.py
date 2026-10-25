"""Builds an art theme for the game: a complete HD folder made from one concept image and a style description.

A theme is a folder with a theme.json:

    {
      "name": "dark-painterly",
      "concept": "concept.png",
      "style": "dark, moody, painterly, muted fantasy strategy map, light from the top left",
      "quality": "medium",
      "pictures": {"BIRTH0": "a subject that replaces the one in pictures.py", "POP": ["one subject per piece", ...]}
    }

`build` generates into THEME/hd, at 4x:
  - the pictures listed in pictures.py: backdrops as whole-image HD files (NAME.png, 1280x800), generated whole with
    any placed objects put into their rectangles; sheets piece by piece, each piece a region file (NAME/X_Y.png, or
    .jpg for opaque scenes), so anything not painted keeps its stand-in art;
  - with --map, the painted map, units and interface as well (generate_painted.py, painted_assets.py, painted_set.py).
Pictures and pieces already built are kept, so an interrupted build can be resumed; --force builds them again.
Generated pieces are kept in THEME/pieces and reused by --force: delete a piece to generate it again.

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
from PIL import Image, ImageOps

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


FRAMING = {
    'object': 'building or object, seen from a high three-quarter angle like a town in a strategy game',
    'figure': 'figure, full body, standing, seen from the front',
    'bust': 'portrait bust, head, shoulders and upper body',
}


def size_for(w, h):
    """The generated image size closest to a rectangle's shape."""
    return '1536x1024' if w >= 1.2 * h else '1024x1536' if h >= 1.2 * w else '1024x1024'


def piece(theme, key, subject, framing, cache, shape=(1, 1)):
    """One object generated alone on magenta and cut out. Kept in the theme's pieces folder, so a picture can be
    composed again without new images; delete a piece to generate it again."""
    if os.path.exists(cache):
        return Image.open(cache).convert('RGBA')
    wide = ' It is wide and low, filling the width of the image.' if shape[0] >= 1.6 * shape[1] else ''
    prompt = (f'Use the reference image only for its art style and colours: {theme["style"]}. '
              f'A single small game {framing}: {subject}, centred and filling most of the image, with a bold, readable '
              f'silhouette that stays clear when shrunk to a small icon.{wide} It is isolated on a perfectly flat, '
              'uniform pure magenta (#FF00FF) background. No ground, no shadow, no text.')
    obj = painted_assets.cut_object(call(key, theme['concept'], prompt, size_for(*shape), theme.get('quality', 'medium')),
                                    size=512)
    if obj is not None:
        os.makedirs(os.path.dirname(cache), exist_ok=True)
        obj.save(cache)
    return obj


def scene(theme, key, subject, rect, cache):
    """An opaque illustration filling a rectangle, kept in the pieces folder like the objects."""
    if os.path.exists(cache):
        return Image.open(cache).convert('RGB')
    w, h = rect[2], rect[3]
    band = ' A wide panoramic composition: keep everything important in the middle band of the picture.' if w >= 2 * h else ''
    prompt = (f'Use the reference image only for its art style and colours: {theme["style"]}. '
              f'A small painted illustration for a game encyclopedia: {subject}. It fills the whole image edge to edge, '
              f'with one clear subject that reads well when small.{band} No text, letters, numbers, frame or border.')
    img = call(key, theme['concept'], prompt, size_for(w, h), theme.get('quality', 'medium')).convert('RGB')
    img = ImageOps.fit(img, (w * S, h * S), Image.LANCZOS)
    os.makedirs(os.path.dirname(cache), exist_ok=True)
    img.save(cache)
    return img


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


def bust(obj, rect):
    """The head and shoulders of a cut-out figure, on a plain dark ground, filling a rectangle."""
    w, h = rect[2] * S, rect[3] * S
    a = np.asarray(obj)
    ys, xs = np.nonzero(a[..., 3] > 128)
    top = ys.min()
    head = xs[ys < top + (ys.max() - top) * 0.12]          # the head is the top of the figure
    ch = int((ys.max() - top) * (0.45 if h > w else 0.2))     # a wide frame gets just the head
    cw = int(ch * w / h)
    cx = int(head.mean())
    crop = obj.crop((cx - cw // 2, max(0, top - ch // 12), cx - cw // 2 + cw, max(0, top - ch // 12) + ch)).resize((w, h), Image.LANCZOS)
    ground = Image.new('RGBA', (w, h), (58, 52, 46, 255))
    ground.alpha_composite(crop)
    return ground


def region(out, name, rect, img, opaque):
    folder = os.path.join(out, name)
    os.makedirs(folder, exist_ok=True)
    if opaque:
        img.convert('RGB').save(os.path.join(folder, f'{rect[0]}_{rect[1]}.jpg'), quality=92)
    else:
        img.save(os.path.join(folder, f'{rect[0]}_{rect[1]}.png'))


def region_done(out, name, rect):
    return any(os.path.exists(os.path.join(out, name, f'{rect[0]}_{rect[1]}.{ext}')) for ext in ('png', 'jpg'))


def sheet(theme, key, name, spec, subjects, out, force):
    """A sheet cut at fixed rectangles: each piece written as its own region file."""
    kind = spec['kind']
    todo = [i for i, p in enumerate(spec['pieces']) if force or not region_done(out, name, p[0])]

    def make(i):
        rect = spec['pieces'][i][0]
        if kind == 'scene':
            return scene(theme, key, subjects[i], rect, cache(theme, name, i))
        return piece(theme, key, subjects[i], FRAMING[kind], cache(theme, name, i), rect[2:])

    with ThreadPoolExecutor(4) as pool:
        made = list(pool.map(lambda i: (i, make(i)), todo))
    for i, img in made:
        if img is None:
            print(f'{name}: piece {i} could not be cut out')
            continue
        rect = spec['pieces'][i][0]
        if kind == 'scene':
            region(out, name, rect, img, True)
            continue
        cell = Image.new('RGBA', (rect[2] * S, rect[3] * S), (0, 0, 0, 0))
        place(cell, img, (0, 0) + tuple(rect[2:]), True)
        region(out, name, rect, cell, False)
        for b in (spec['pieces'][i][2] if len(spec['pieces'][i]) > 2 else []):
            region(out, name, b, bust(img, b), True)
    for rect in spec.get('clear', []) if made else []:
        region(out, name, rect, Image.new('RGBA', (rect[2] * S, rect[3] * S), (0, 0, 0, 0)), False)
    return len(made)


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

    for name, spec in pictures.SHEETS.items():
        if only and name not in only:
            continue
        subjects = overrides.get(name, [p[1] for p in spec['pieces']])
        try:
            count = sheet(theme, key, name, spec, subjects, out, force)
            print(f'{name}: {count} of {len(spec["pieces"])} pieces written' if count else f'{name}: kept')
        except Exception as e:
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
        for name, spec in pictures.SHEETS.items():
            print(f'{name:10} {spec["kind"]:8}  {len(spec["pieces"])} pieces')
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
