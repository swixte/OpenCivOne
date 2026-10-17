"""Builds a stand-in data set, so OpenCivOne can start without the original DOS game files.

Nothing here comes from the original game. The map sheets (TER257, SP257, SP299) are the HD art from
tools/hdart reduced to the original size; every other picture is a labelled placeholder; the text
files hold short generic text under every key the code asks for.

Usage: python3 make_standin.py OUT_DIR
Writes OUT_DIR/raw/NAME.idx (indexed pictures) and OUT_DIR/palette.bin for the PicWriter tool, and
the text files and CIV.EXE placeholder straight into OUT_DIR. See build.sh.
"""

import os
import re
import sys
import struct
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
sys.path.insert(0, os.path.join(ROOT, 'tools', 'hdart'))

import common as c          # noqa: E402
import generate             # noqa: E402

# ---------- palette: the 16 EGA colors the interface uses, a 6x6x6 color cube, and greys ----------

EGA = [0x000000, 0x0000a7, 0x00a700, 0x00a7a7, 0xa70000, 0xa700a7, 0xa75300, 0xa7a7a7,
       0x535353, 0x5353fb, 0x53fb53, 0x53fbfb, 0xfb5353, 0xfb53fb, 0xfbfb53, 0xfbfbfb]


def make_palette():
    pal = [((v >> 16) & 0xff, (v >> 8) & 0xff, v & 0xff) for v in EGA]
    steps = [0, 51, 102, 153, 204, 255]
    for r in steps:
        for g in steps:
            for b in steps:
                pal.append((r, g, b))
    for i in range(24):
        v = 8 + i * 10
        pal.append((v, v, v))
    return np.array(pal, dtype=np.int64)


PALETTE = make_palette()
# art uses indexes 16..255, except those the game changes while running: 45 is set to a fixed brown and
# 96..127 cycle (the ocean shimmer). 0..15 stay for the interface and player color swaps.
ART_INDEXES = np.array([i for i in range(16, 256) if i != 45 and not 96 <= i <= 127])
ART = PALETTE[ART_INDEXES]


def quantize(rgba, transparent=True):
    """Nearest art palette index for every pixel; transparent pixels become index 0."""
    rgb = rgba[..., :3].astype(np.int64)
    d = ((rgb[..., None, :] - ART[None, None, :, :]) ** 2).sum(-1)
    idx = ART_INDEXES[d.argmin(-1)]
    if transparent:
        idx = np.where(rgba[..., 3] < 128, 0, idx)
    return idx.astype(np.uint8)


def reduce(img):
    """2x HD art to original size: each 2x2 block becomes its most opaque, first-found color."""
    h, w = img.shape[0] // 2, img.shape[1] // 2
    out = np.zeros((h, w, 4), dtype=np.uint8)
    for dy in (1, 0):
        for dx in (1, 0):
            px = img[dy::2, dx::2][:h, :w]
            take = px[..., 3] >= out[..., 3]
            out[take] = px[take]
    return out


def key_indexes(img, primary, secondary):
    """Original pixels under key colors: the key's color index, so player color swaps apply."""
    small = reduce(img)
    r, g, b, a = (small[..., k].astype(np.int64) for k in range(4))
    prim = (a == 255) & (g == 0) & (r == b) & (r > 0)
    sec = (a == 255) & (r == 0) & (g == b) & (g > 0)
    idx = quantize(small)
    idx[prim] = primary
    idx[sec] = secondary
    return idx


# ---------- a tiny 3x5 font for placeholder labels ----------

FONT = {'A': "010101111101101", 'B': "110101110101110", 'C': "011100100100011", 'D': "110101101101110", 'E': "111100110100111",
        'F': "111100110100100", 'G': "011100101101011", 'H': "101101111101101", 'I': "111010010010111", 'J': "001001001101010",
        'K': "101101110101101", 'L': "100100100100111", 'M': "101111111101101", 'N': "110101101101101", 'O': "010101101101010",
        'P': "110101110100100", 'Q': "010101101110011", 'R': "110101110101101", 'S': "011100010001110", 'T': "111010010010010",
        'U': "101101101101111", 'V': "101101101101010", 'W': "101101111111101", 'X': "101101010101101", 'Y': "101101010010010",
        'Z': "111001010100111", '0': "111101101101111", '1': "010110010010111", '2': "110001010100111", '3': "110001010001110",
        '4': "101101111001001", '5': "111100110001110", '6': "011100111101111", '7': "111001010010010", '8': "111101111101111",
        '9': "111101111001110", '.': "000000000000010", '-': "000000111000000", ' ': "000000000000000"}


def text(canvas, x, y, s, color, scale=1):
    for n, ch in enumerate(s.upper()):
        bits = FONT.get(ch, FONT[' '])
        for b in range(15):
            if bits[b] == '1':
                px, py = x + (n * 4 + b % 3) * scale, y + (b // 3) * scale
                canvas[py:py + scale, px:px + scale] = color


def placeholder(name, w=320, h=200, hue=0):
    """A dark screen with a soft colored glow and the file's name, so each screen can be told apart."""
    yy, xx = np.mgrid[0:h, 0:w]
    base = np.array([(40, 60, 110), (80, 50, 40), (40, 90, 60), (90, 70, 30), (70, 40, 90)][hue % 5])
    glow = np.clip(1 - np.hypot(xx - w / 2, yy - h / 2) / (max(w, h) * 0.7), 0, 1)[..., None]
    rgb = (base * (0.25 + glow * 0.9)).astype(np.uint8)
    img = np.concatenate([rgb, np.full((h, w, 1), 255, np.uint8)], -1)
    idx = quantize(img, transparent=False)
    if w >= 120 and h >= 40:
        label = os.path.splitext(name)[0]
        scale = 3 if w >= 200 else 1
        tw = len(label) * 4 * scale
        text(idx, (w - tw) // 2, h // 2 - 4 * scale, label, 15, scale)
        text(idx, (w - 13 * 4) // 2, h // 2 + 4 * scale, 'STAND-IN ART', 7)
    return idx


# ---------- picture files ----------

def sheet(name):
    """TER257, SP257 and SP299 with the HD art reduced to original size at the same places."""
    canvas = np.zeros((200, 320), dtype=np.uint8)
    for sheet_name, x, y, suffix, img in generate.sprites():
        if sheet_name != name:
            continue
        m = re.match(r'(?:_t(\d+))?(?:_k(\d+)-(\d+))?$', suffix)
        up = int(m.group(1) or 0)
        img = img[up * 2:]
        primary = int(m.group(2)) if m.group(2) else 10
        secondary = int(m.group(3)) if m.group(3) else 2
        idx = key_indexes(img, primary, secondary)
        h, w = idx.shape
        canvas[y:y + h, x:x + w] = idx
    if name == 'SP257':
        _sp257_extras(canvas)
    if name == 'SP299':
        _sp299_extras(canvas)
    return canvas


def _sp257_extras(canvas):
    # mouse cursors (16x16 at x 0..63, y 32): a white arrow with a black edge
    for i in range(4):
        for yy in range(12):
            for xx in range(yy // 2 + 1 if i == 0 else 8):
                canvas[33 + yy, i * 16 + 1 + xx] = 15 if xx < (yy // 2 if i == 0 else 7) else 0
    # small city screen icons, 7x7 at (129 + 8 * (i % 4), 33 + 8 * (i / 4)): food, shield, trade, ...
    colors = [10, 12, 14, 11, 13, 9, 15, 12]
    for i in range(8):
        x0, y0 = (i & 3) * 8 + 129, (i & 4) * 2 + 33
        canvas[y0 + 1:y0 + 6, x0 + 1:x0 + 6] = colors[i]
        canvas[y0 + 1, x0 + 1] = canvas[y0 + 1, x0 + 5] = 0
    # citizens 8x16 at (8 * i, 128)
    for i in range(9):
        x0 = i * 8
        canvas[129:132, x0 + 2:x0 + 6] = 12 + (i % 3)
        canvas[132:140, x0 + 1:x0 + 7] = [10, 14, 12, 9, 11, 13, 15, 7, 6][i]
        canvas[140:143, x0 + 2:x0 + 6] = 6
    # palace pieces 7x15 at (160 + 7 * i, 0, 16, 32)
    for row in range(3):
        for i in range(20):
            x0, y0 = 160 + 7 * i, row * 16
            canvas[y0 + 4:y0 + 15, x0:x0 + 7] = [7, 8, 6][row]
            canvas[y0 + 2:y0 + 4, x0 + 1:x0 + 6] = 4
    # news picture 20x9 at (176, 128)
    canvas[128:137, 176:196] = 7
    text(canvas, 178, 130, 'NEWS', 0)


def _sp299_extras(canvas):
    # the stone pattern behind side panels and dialogs (32x16 at 288, 120) and the city screen pattern (16x16 at 208, 100)
    yy, xx = np.mgrid[0:16, 0:32]
    stone = np.where((yy % 8 == 7) | ((xx + (yy // 8) * 8) % 16 == 15), 8, np.where(c.hash2(xx, yy, 901) < 0.04, 15, 7))
    canvas[120:136, 288:320] = stone
    yy, xx = np.mgrid[0:16, 0:16]
    canvas[100:116, 208:224] = np.where((xx + yy) % 8 == 0, 9, 1)
    # food icons, 8x8 at (192..255, 120..135)
    for x0 in range(192, 256, 8):
        for y0 in (120, 128):
            canvas[y0 + 1:y0 + 7, x0 + 3:x0 + 5] = 14
            canvas[y0 + 2:y0 + 5, x0 + 2:x0 + 6] = 14
            canvas[y0 + 6, x0 + 3:x0 + 5] = 6
    # city improvement icons, 18x10 in a grid from (161, 50)
    for col in range(8):
        for row in range(5):
            x0, y0 = 161 + col * 19, 50 + row * 10
            canvas[y0:y0 + 10, x0:x0 + 18] = 8
            canvas[y0 + 1:y0 + 9, x0 + 1:x0 + 17] = [9, 12, 10, 14, 11, 13, 6, 3][col]
            text(canvas, x0 + 6, y0 + 3, str((col * 5 + row) % 10), 15)
    # three advisor portraits 41x58 at (160 + 42 * i, 142) and screen-2 pictures 40x60 at (160..319, 140)
    for i in range(3):
        x0 = 160 + 42 * i
        canvas[142:200, x0:x0 + 41] = [9, 2, 4][i]
        yy, xx = np.mgrid[0:58, 0:41]
        face = (((xx - 20) / 11) ** 2 + ((yy - 22) / 14) ** 2) <= 1
        canvas[142:200, x0:x0 + 41][face] = 14
        body = (yy > 40) & (np.abs(xx - 20) < 16)
        canvas[142:200, x0:x0 + 41][body] = [1, 6, 5][i]
    for x0 in (200, 240, 280):
        if canvas[140, x0] == 0:
            canvas[140:200, x0:x0 + 40] = np.where(canvas[140:200, x0:x0 + 40] == 0, 8, canvas[140:200, x0:x0 + 40])


REQUIRED_PICS = """adscreen arch back0a back0m back1a back1m back2a back2m back3a birth0 birth1 birth2 birth3 birth4 birth5 birth6
birth7 birth8 castle0 castle1 castle2 castle3 castle4 cback cbacks1 cbacks2 cbacks3 cbrush0 cbrush1 cbrush2 cbrush3 cbrush4 cbrush5
citypix1 citypix2 citypix3 custom diffs discovr1 discovr2 docker govt0a govt0m govt1a govt1m govt2a govt2m govt3a hill iconpg1 iconpg2
iconpg3 iconpg4 iconpg5 iconpg6 iconpg7 iconpg8 iconpga iconpgb iconpgc iconpgd iconpge iconpgt1 iconpgt2 invader2 invader3 invaders
king00 king01 king02 king03 king04 king05 king06 king07 king08 king09 king10 king11 king12 king13 kink00 kink03 logo love1 love2 map
nuke1 planet1 planet2 pop riot riot2 sad settlers slag2 slam1 slam2 sp257 sp299 spacest sprites ter257 torch wonders wonders2""".split()

REQUIRED_PALS = """back0a back0m back1a back1m back2a back2m back3a birth0 birth1 birth2 birth3 birth4 birth5 birth6 birth7 birth8
discovr1 discovr2 hill iconpg1 iconpga king00 king01 king02 king03 king04 king05 king06 king07 king08 king09 king10 king11 king12
king13 slam1 sp256 sp257""".split()


def torch():
    """The mouse cursor: a small torch-shaped arrow."""
    idx = np.zeros((16, 16), dtype=np.uint8)
    for y in range(12):
        for x in range(y // 2 + 1):
            idx[y, x] = 15 if x < y // 2 else 0
    idx[12:16, 3:5] = 6
    return idx


def pictures():
    for n, name in enumerate(REQUIRED_PICS):
        upper = name.upper()
        if upper in ('TER257', 'SP257', 'SP299'):
            yield upper, sheet(upper)
        elif upper == 'TORCH':
            yield upper, torch()
        elif upper.startswith('CBRUSH'):
            yield upper, placeholder(upper, 48, 32, n)
        else:
            yield upper, placeholder(upper, 320, 200, n)


# ---------- text files ----------

def code_keys():
    """Every *KEY literal in the game code, plus keys the code builds at run time."""
    keys = set()
    for folder, _, files in os.walk(os.path.join(ROOT, 'src')):
        for f in files:
            if f.endswith('.cs'):
                keys |= set(re.findall(r'"(\*[A-Z0-9_ ]+)"', open(os.path.join(folder, f), encoding='utf-8', errors='ignore').read()))
    keys |= {f'*NEWS{chr(ord("A") + i)}' for i in range(14)}
    keys |= {'*TEMPERATURE', '*LAND', '*CLIMATE', '*AGE'}
    return sorted(keys)


def game_names():
    """Unit, improvement, wonder and advance names from the game's definitions."""
    names = set()
    for folder, _, files in os.walk(os.path.join(ROOT, 'src', 'Game')):
        for f in files:
            if f.endswith('.cs'):
                src = open(os.path.join(folder, f), encoding='utf-8', errors='ignore').read()
                names |= set(re.findall(r'new \w+Definition\(\s*[\w.]+\s*,\s*"([^"]+)"', src))
    return sorted(names)


def text_file(keys, body):
    """A language file: a 0x212 byte header (the original's index, unused here), then *KEY entries."""
    header = (b' ' * 0x211) + b'\n'
    lines = []
    for k in keys:
        lines.append(k)
        lines.append(body(k))
        lines.append('')
    lines.append('*END')
    return header + ('\r\n'.join(lines) + '\r\n').encode('ascii', 'replace')


def generic(key):
    k = key.lstrip('*').replace('_', ' ').title()
    return f'{k}: stand-in text. The original game text is not included.'


def write_texts(out):
    keys = code_keys()
    names = ['*' + n.upper() for n in game_names()]
    for section in ('KING', 'HELP', 'ERROR', 'INTRO', 'INTRO3'):
        open(os.path.join(out, f'{section}.TXT'), 'wb').write(text_file(keys, generic))
    open(os.path.join(out, 'PRODUCE.TXT'), 'wb').write(text_file(names + keys, generic))
    blurb_keys = sorted(set(names + [n + '2' for n in names] + keys))
    for i in range(5):
        open(os.path.join(out, f'BLURB{i}.TXT'), 'wb').write(text_file(blurb_keys, generic))
    story = ['In the beginning the Earth was without form, and void.', 'Stand-in story text.'] * 8
    open(os.path.join(out, 'STORY.TXT'), 'w', newline='\r\n').write('\n'.join(story) + '\n')
    credits = ['OpenCivOne', 'Stand-in data set', 'Map art generated by tools/hdart']
    open(os.path.join(out, 'CREDITS.TXT'), 'w', newline='\r\n').write('\n'.join(credits) + '\n')


def main(out):
    raw = os.path.join(out, 'raw')
    os.makedirs(raw, exist_ok=True)
    pal6 = (PALETTE * 63 // 255).astype(np.uint8)
    open(os.path.join(out, 'palette.bin'), 'wb').write(pal6.tobytes())
    count = 0
    for name, idx in pictures():
        h, w = idx.shape
        open(os.path.join(raw, f'{name}.idx'), 'wb').write(struct.pack('<HH', w, h) + idx.astype(np.uint8).tobytes())
        count += 1
    open(os.path.join(raw, 'pals.txt'), 'w').write('\n'.join(p.upper() for p in REQUIRED_PALS) + '\n')
    write_texts(out)
    open(os.path.join(out, 'CIV.EXE'), 'wb').write(b'MZ stand-in, not an executable\n')
    print(f'{count} pictures, {len(REQUIRED_PALS)} palettes and text files prepared in {out}')


if __name__ == '__main__':
    main(sys.argv[1])
