"""Regrades a theme's painted ground textures to a quieter palette, keeping the painting.

Each texture is split into its brightness and its colour. The brightness keeps the painted strokes but is moved to the
terrain's value band, its broad swings (blotches across the tile) and its fine detail (brush texture) each turned down
by their own factor. The colour keeps a little of the painting's own variation around a muted target colour. So the
sea becomes a quiet slate, the open land sage, straw and sand, and forest a darker mass, each in its own band of
darkness: a map that reads in grey before it reads in colour.

Usage: python3 regrade.py GROUND_DIR [OUT_DIR] [--palette muted] [--sheet SHEET.png]
       python3 regrade.py --sprites PAINTED_DIR [--palette muted]
  GROUND_DIR  a theme's hd/painted/ground folder (desert.png, grass.png, ocean.png, ...)
  OUT_DIR     where the regraded textures go (default: over the originals)
  --sheet     also writes a before/after sheet, in colour and in grey
  --sprites   regrades the sprite sets in a theme's hd/painted folder in place (mountains, jungle, trees): less colour,
              highlights pressed down

Regrade from the original art: run it on textures already regraded and the changes add up.
"""

import os
import sys

import numpy as np
from PIL import Image, ImageDraw

# target colour (its brightness is the terrain's value band), how much of the painting's own colour variation stays,
# how much of its broad brightness swings stay, and how much of its fine brush detail stays
PALETTES = {
    'muted': {
        'ocean':     ((72, 92, 104), 0.30, 0.45, 0.55),
        'shallow':   ((104, 128, 126), 0.30, 0.45, 0.55),
        'forest':    ((62, 76, 54), 0.40, 0.70, 0.80),
        'jungle':    ((56, 74, 50), 0.40, 0.70, 0.80),
        'floor':     ((86, 90, 66), 0.40, 0.60, 0.70),
        'swamp':     ((96, 102, 80), 0.40, 0.60, 0.70),
        'grass':     ((122, 136, 92), 0.40, 0.70, 0.75),
        'hills':     ((136, 130, 100), 0.40, 0.70, 0.80),
        'plains':    ((172, 152, 98), 0.45, 0.70, 0.75),
        'tundra':    ((146, 142, 126), 0.40, 0.60, 0.70),
        'mountains': ((132, 126, 114), 0.45, 0.80, 0.85),
        'desert':    ((200, 184, 144), 0.35, 0.65, 0.70),
        'snow':      ((214, 218, 216), 0.25, 0.60, 0.70),
    },
}

LUMA = np.array([0.299, 0.587, 0.114], np.float32)


def blur(channel, radius):
    """A Gaussian blur of a float channel; the textures tile, so it wraps around the edges (done in the frequency domain)."""
    h, w = channel.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.fftfreq(w)[None, :]
    kernel = np.exp(-2 * (np.pi * radius) ** 2 * (fx ** 2 + fy ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(channel) * kernel)).astype(np.float32)


def regrade(rgb, target, colour_keep, broad_keep, fine_keep):
    """The texture moved to its target colour and value band, its painting kept at the given strengths."""
    luma = rgb @ LUMA
    broad = blur(luma, 12)
    fine = luma - broad
    target = np.array(target, np.float32)
    target_luma = float(target @ LUMA)

    new_luma = target_luma + (broad - broad.mean()) * broad_keep + fine * fine_keep

    # colour: the painting's own variation around its mean colour, turned down, over the target colour
    chroma = rgb - luma[..., None]
    chroma = (chroma - chroma.reshape(-1, 3).mean(0)) * colour_keep
    target_chroma = target - target_luma
    out = new_luma[..., None] + target_chroma + chroma
    return out.clip(0, 255)


# sprite sets (folders of cut-out pictures over the ground): how much colour stays, and the brightness above which
# highlights are pressed down (to `knee` plus a part of what was above it), so no sprite outshines the quiet ground
SPRITES = {
    'muted': {
        'mountains': (0.65, 150, 0.5),
        'jungle':    (0.70, 150, 0.7),
        'trees':     (0.80, 160, 0.8),
    },
}


def regrade_sprite(rgba, colour_keep, knee, above):
    """A cut-out picture with less colour and its highlights pressed down; its painting and transparency stay."""
    rgb, alpha = rgba[..., :3], rgba[..., 3:]
    luma = rgb @ LUMA
    new_luma = np.where(luma > knee, knee + (luma - knee) * above, luma)
    chroma = (rgb - luma[..., None]) * colour_keep
    return np.concatenate([(new_luma[..., None] + chroma).clip(0, 255), alpha], -1)


def regrade_sprites(painted_dir, palette):
    """Regrades the sprite sets in a theme's hd/painted folder in place."""
    for folder, (colour_keep, knee, above) in SPRITES[palette].items():
        path = os.path.join(painted_dir, folder)
        if not os.path.isdir(path):
            continue
        for name in sorted(os.listdir(path)):
            if not name.endswith('.png'):
                continue
            img = np.asarray(Image.open(os.path.join(path, name)).convert('RGBA'), np.float32)
            Image.fromarray(regrade_sprite(img, colour_keep, knee, above).astype(np.uint8), 'RGBA').save(os.path.join(path, name))
        print(f'{folder}: sprites regraded')


def stats(rgb):
    luma = rgb @ LUMA
    return f'L {luma.mean():5.1f} sd {luma.std():4.1f}  chroma {(rgb.max(-1) - rgb.min(-1)).mean():5.1f}'


def main():
    args = sys.argv[1:]
    palette, sheet = 'muted', None
    if '--palette' in args:
        i = args.index('--palette')
        palette = args[i + 1]
        del args[i:i + 2]
    if '--sheet' in args:
        i = args.index('--sheet')
        sheet = args[i + 1]
        del args[i:i + 2]
    if '--sprites' in args:
        i = args.index('--sprites')
        regrade_sprites(args[i + 1], palette)
        del args[i:i + 2]
        if not args:
            return
    if not args or len(args) > 2:
        sys.exit(__doc__)
    src = args[0]
    out = args[1] if len(args) > 1 else src
    os.makedirs(out, exist_ok=True)
    targets = PALETTES[palette]
    pairs = []
    for name, (target, colour_keep, broad_keep, fine_keep) in targets.items():
        path = os.path.join(src, name + '.png')
        if not os.path.exists(path):
            continue
        img = Image.open(path)
        alpha = img.getchannel('A') if img.mode == 'RGBA' else None
        rgb = np.asarray(img.convert('RGB'), np.float32)
        new = regrade(rgb, target, colour_keep, broad_keep, fine_keep)
        result = Image.fromarray(new.astype(np.uint8), 'RGB')
        if alpha is not None:
            result.putalpha(alpha)
        result.save(os.path.join(out, name + '.png'))
        print(f'{name:10s} before {stats(rgb)}   after {stats(new)}')
        pairs.append((name, Image.fromarray(rgb.astype(np.uint8)), result.convert('RGB')))
    if sheet:
        tile = 160
        rows = [('before', 1), ('after', 2)]
        w = tile * len(pairs)
        canvas = Image.new('RGB', (w, tile * 4 + 4 * 18), (30, 30, 30))
        draw = ImageDraw.Draw(canvas)
        for r, (grey, (label, k)) in enumerate([(g, row) for g in (False, True) for row in rows]):
            y = r * (tile + 18)
            draw.text((4, y + 3), f'{label}{" (grey)" if grey else ""}', fill=(230, 230, 230))
            for c, pair in enumerate(pairs):
                im = pair[k].crop((0, 0, 256, 256)).resize((tile, tile), Image.LANCZOS)
                if grey:
                    im = im.convert('L').convert('RGB')
                canvas.paste(im, (c * tile, y + 18))
        for c, pair in enumerate(pairs):
            draw.text((c * tile + 4, 18 + 4), pair[0], fill=(255, 255, 255))
        canvas.save(sheet)


if __name__ == '__main__':
    main()
