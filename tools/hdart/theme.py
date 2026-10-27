"""Builds an art theme for the game: a complete HD folder made from one concept image and a style description.

A theme is a folder with a theme.json:

    {
      "name": "dark-painterly",
      "concept": "concept.png",
      "style": "dark, moody, painterly, muted fantasy strategy map, light from the top left",
      "quality": "medium",
      "references": {"default": ["refs/a.jpg"], "scene": ["refs/b.jpg", "refs/c.jpg"]},
      "rules": {"scene": "rules for Encyclopedia scenes", "bust": "rules for leader portraits"},
      "pictures": {"BIRTH0": "a subject that replaces the one in pictures.py", "POP": ["one subject per piece", ...]}
    }

`build` generates into THEME/hd, at 4x:
  - the pictures listed in pictures.py: backdrops as whole-image HD files (NAME.png, 1280x800), generated whole with
    any placed objects put into their rectangles; sheets piece by piece, each piece a region file (NAME/X_Y.png, or
    .jpg for opaque scenes), so anything not painted keeps its stand-in art;
  - with --map, the painted map, units and interface as well (generate_painted.py, painted_assets.py, painted_set.py).
Style references and rules can be given per kind of asset (backdrop, scene, object, figure, bust); without them the
concept image is the reference. Pictures and pieces already built are kept, so an interrupted build can be resumed; --force builds them again.
Generated pieces are kept in THEME/pieces and reused by --force: delete a piece to generate it again.

`install` puts a theme into the game's themes/NAME folder: the shared 4x set (assets/hd4 and assets/painted) with the
theme's own files from THEME/hd over it, so a theme only holds what it changes. Then set "Theme": "NAME" in
~/Games/OpenCivOne/Preferences.json.

Usage:
  OPENAI_API_KEY=... python3 theme.py build THEME_DIR [--map] [--only NAME|NAME:PIECE ...] [--force]
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


def call(key, references, prompt, size, quality):
    """One image from the OpenAI Images API, with the reference images as style references."""
    files = [('image[]', (os.path.basename(path), open(path, 'rb'),
                          'image/jpeg' if path.lower().endswith('.jpg') else 'image/png')) for path in references]
    try:
        r = requests.post('https://api.openai.com/v1/images/edits', headers={'Authorization': f'Bearer {key}'},
                          data={'model': 'gpt-image-2', 'prompt': prompt, 'size': size, 'quality': quality, 'n': '1'},
                          files=files, timeout=900)
    finally:
        for _, (_, f, _) in files:
            f.close()
    if r.status_code != 200:
        raise RuntimeError(f'HTTP {r.status_code} {r.text[:300]}')
    return Image.open(io.BytesIO(base64.b64decode(r.json()['data'][0]['b64_json'])))


def references(theme, kind):
    """The style reference images for a kind of asset: the theme's own list for that kind, its default list, or the
    concept image."""
    refs = theme.get('references', {})
    paths = refs.get(kind) or refs.get('default') or [theme['concept']]
    return [os.path.join(theme['dir'], p) for p in paths]


def lead(theme, kind):
    """The start of every prompt: the theme's style and its rules for this kind of asset."""
    rules = theme.get('rules', {}).get(kind, '')
    return (f'Use the reference images only for their painting style, palette and light, never for their subjects or '
            f'composition. Style: {theme["style"]}. {rules} ')


def backdrop(theme, key, name, subject, calm):
    prompt = (lead(theme, 'backdrop') +
              f'A wide full-screen game illustration: {subject} {calm} No text, letters, numbers, frame or border.')
    img = call(key, references(theme, 'backdrop'), prompt, '1536x1024', theme.get('quality', 'medium')).convert('RGB')
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


def piece(theme, key, subject, framing, cache, shape=(1, 1), kind='object'):
    """One object generated alone on magenta and cut out. Kept in the theme's pieces folder, so a picture can be
    composed again without new images; delete a piece to generate it again."""
    if os.path.exists(cache):
        return Image.open(cache).convert('RGBA')
    wide = ' It is wide and low, filling the width of the image.' if shape[0] >= 1.6 * shape[1] else ''
    prompt = (lead(theme, kind) +
              f'A single small game {framing}: {subject}, centred and filling most of the image, with a bold, readable '
              f'silhouette that stays clear when shrunk to a small icon.{wide} It is isolated on a perfectly flat, '
              'uniform pure magenta (#FF00FF) background. No ground, no shadow, no text.')
    obj = painted_assets.cut_object(call(key, references(theme, kind), prompt, size_for(*shape), theme.get('quality', 'medium')),
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
    prompt = (lead(theme, 'scene') +
              f'A small painted illustration for a game encyclopedia: {subject}. It fills the whole image edge to edge, '
              f'with one clear subject that reads well when small.{band} No text, letters, numbers, frame or border.')
    img = call(key, references(theme, 'scene'), prompt, size_for(w, h), theme.get('quality', 'medium')).convert('RGB')
    img = ImageOps.fit(img, (w * S, h * S), Image.LANCZOS)
    os.makedirs(os.path.dirname(cache), exist_ok=True)
    img.save(cache, quality=92)
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
        rx = min(fitted.width * 0.55, w * 0.46)     # the shadow stays inside the rectangle
        style.shadow(a, px + fitted.width / 2, py + fitted.height - S, rx, fitted.height * 0.12, 110)
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
    # some rectangles reach past the 320x200 picture (the game trims them when drawing); a region has to fit
    img = img.crop((0, 0, min(rect[2], 320 - rect[0]) * S, min(rect[3], 200 - rect[1]) * S))
    if opaque:
        img.convert('RGB').save(os.path.join(folder, f'{rect[0]}_{rect[1]}.jpg'), quality=92)
    else:
        img.save(os.path.join(folder, f'{rect[0]}_{rect[1]}.png'))


def region_done(out, name, rect):
    return any(os.path.exists(os.path.join(out, name, f'{rect[0]}_{rect[1]}.{ext}')) for ext in ('png', 'jpg'))


def frames(theme, key, name, spec, out, force):
    """An animation: one group generated once, every frame made from it."""
    rects = [p[0] for p in spec['pieces']]
    if not force and all(region_done(out, name, r) for r in rects):
        return 0
    w, h = rects[0][2], rects[0][3]
    walk = spec['motion'] == 'walk'
    obj = piece(theme, key, spec['pieces'][0][1], 'group of figures' if walk else 'object', cache(theme, name, 0), (w, h),
                'figure' if walk else 'object')
    if obj is None:
        return 0
    n = len(rects)
    for f, rect in enumerate(rects):
        cell = Image.new('RGBA', (w * S, h * S), (0, 0, 0, 0))
        if spec['motion'] == 'walk':
            # every other frame half a pixel higher, so the group bobs as it walks
            place(cell, obj, (0, 0, w, h), True)
            cell = cell.transform(cell.size, Image.AFFINE, (1, 0, 0, 0, 1, S // 2 if f % 2 else 0))
        else:
            # the blast grows from the ground over the first half and fades over the second
            grow = min(1.0, 0.15 + f / (n * 0.5))
            gw, gh = max(1, round(w * grow)), max(1, round(h * grow))
            place(cell, obj, ((w - gw) // 2, h - gh, gw, gh), True)
            fade = 1.0 if f < n * 0.6 else max(0.0, 1 - (f - n * 0.6) / (n * 0.4))
            a = np.asarray(cell).copy()
            a[..., 3] = (a[..., 3] * fade).astype(np.uint8)
            cell = Image.fromarray(a)
        region(out, name, rect, cell, False)
    return 1


def sheet(theme, key, name, spec, subjects, out, force, chosen=None):
    """A sheet cut at fixed rectangles: each piece written as its own region file. chosen limits it to some pieces."""
    kind = spec['kind']
    mirror = spec.get('mirror', {})
    todo = [i for i, p in enumerate(spec['pieces']) if i not in mirror and (chosen is None or i in chosen) and
            (force or not region_done(out, name, p[0]))]

    def make(i):
        rect = spec['pieces'][i][0]
        try:
            if kind == 'scene':
                return scene(theme, key, subjects[i], rect, cache(theme, name, i)[:-4] + '.jpg')
            return piece(theme, key, subjects[i], FRAMING[kind], cache(theme, name, i), rect[2:], kind)
        except Exception as e:      # a failed piece is left for the next run
            print(f'{name}: piece {i} failed, {e}', flush=True)
            return None

    with ThreadPoolExecutor(6) as pool:
        made = list(pool.map(lambda i: (i, make(i)), todo))
    made += [(i, ImageOps.mirror(img)) for i, j in mirror.items() for k, img in list(made) if k == j and img is not None]
    for i, img in made:
        if img is None:
            continue
        rect = spec['pieces'][i][0]
        if kind == 'scene':
            region(out, name, rect, img, True)
            continue
        cell = Image.new('RGBA', (rect[2] * S, rect[3] * S), (0, 0, 0, 0))
        # buildings get a soft contact shadow, which the game blends onto the landscape under them
        place(cell, img, (0, 0) + tuple(rect[2:]), True, shade=kind == 'object')
        region(out, name, rect, cell, False)
        for b in (spec['pieces'][i][2] if len(spec['pieces'][i]) > 2 else []):
            region(out, name, b, bust(img, b), True)
    for rect in spec.get('clear', []) if any(img is not None for _, img in made) else []:
        region(out, name, rect, Image.new('RGBA', (rect[2] * S, rect[3] * S), (0, 0, 0, 0)), False)
    return sum(1 for _, img in made if img is not None)


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
    # NAME:i in only picks single pieces of a sheet
    pieces = {}
    for item in list(only or []):
        if ':' in item:
            name, i = item.split(':')
            pieces.setdefault(name, set()).add(int(i))
            only.discard(item)
            only.add(name)
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
        env = dict(os.environ, PAINTED_STYLE=theme['style'],
                   PAINTED_REFS=','.join(os.path.join(theme_dir, r) for r in theme.get('map_references', [])),
                   PAINTED_PROMPTS=os.path.join(theme_dir, theme['map_prompts']) if theme.get('map_prompts') else '')
        for cmd in (['generate_painted.py', theme['concept'], raw], ['painted_assets.py', raw, os.path.join(out, 'painted')],
                    ['painted_set.py', out, os.path.join(out, 'painted', 'objects')]):
            subprocess.run([sys.executable, os.path.join(HERE, cmd[0])] + cmd[1:], check=True, env=env)

    jobs = []
    for name, (subject, calm) in pictures.BACKDROPS.items():
        def make(n=name, s=subject, c=calm):
            img = backdrop(theme, key, n, overrides.get(n, s), c)
            return placed(theme, key, n, img, pictures.PLACED[n]) if n in pictures.PLACED else img
        jobs.append((name, make))

    def run(job):
        name, make = job
        path = os.path.join(out, name + '.jpg')      # backdrops have no transparency
        if (os.path.exists(path) or os.path.exists(path[:-4] + '.png')) and not force:
            return f'{name}: kept'
        try:
            make().convert('RGB').save(path, quality=92)
            if os.path.exists(path[:-4] + '.png'):
                os.remove(path[:-4] + '.png')
            return f'{name}: written'
        except Exception as e:      # one failed picture should not stop the rest
            return f'{name}: failed, {e}'

    jobs = [j for j in jobs if j[0] not in pictures.NEVER_PAINT and not (only and j[0] not in only)]
    with ThreadPoolExecutor(4) as pool:
        for line in pool.map(run, jobs):
            print(line, flush=True)

    for name, spec in pictures.SHEETS.items():
        if only and name not in only:
            continue
        subjects = overrides.get(name, [p[1] for p in spec['pieces']])
        chosen = pieces.get(name)
        try:
            count = (frames(theme, key, name, spec, out, force) if spec['kind'] == 'frames'
                     else sheet(theme, key, name, spec, subjects, out, force, chosen))
            print(f'{name}: {count} of {len(spec["pieces"])} pieces written' if count else f'{name}: kept', flush=True)
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
