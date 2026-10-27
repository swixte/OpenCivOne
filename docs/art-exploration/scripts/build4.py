import sys
from PIL import Image, ImageDraw, ImageFont
import comp2
from states import Scene, CELL, SANS, INK, irrigation, roads, fog, goto_path

OUT = '/opt/cursor/artifacts/moodboard/ukiyo-factions'
import os
os.makedirs(OUT, exist_ok=True)


def save(s, name, sel_kind, sel_cell, portrait):
    s.screen(sel_kind, sel_cell, portrait=portrait).save(f'{OUT}/{name}.jpg', quality=93)
    s.screen(sel_kind, sel_cell, grid=True, portrait=portrait).save(f'{OUT}/{name}-grid.jpg', quality=93)


def roman_ancient():
    s = Scene(True)
    irrigation(s.canvas, [(5, 2), (4, 3)])
    roads(s.canvas, [[(5, 3), (4, 4), (4, 5), (3, 6)], [(5, 3), (6, 2)]])
    s.obj('horses', 4, 7, fit=0.5)
    s.obj('fish', 8, 4, fit=0.5)
    s.obj('hut', 1, 3, fit=0.5)
    s.obj('mine', 0, 5, fit=0.52)
    fog(s.canvas, [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (0, 2), (0, 3), (9, 6), (9, 7), (8, 7)])
    goto_path(s.canvas, [(4, 5), (3, 4), (2, 3), (2, 2)])
    s.city('rcity', 5, 3, 'p', size=8, capital=True, garrison=2)
    s.city('rtown', 3, 6, 'p', size=4, disorder=True)
    s.city('rtown', 7, 1, 'r', size=3)
    s.unit('legion', 6, 2, 'p', state=('fortified', 'veteran'))
    s.unit('legion', 7, 0, 'r')
    s.unit('cavalry', 8, 2, 'r')
    s.unit('legion', 4, 6, 'p', stack=3, stack_style='badge')
    s.unit('rsettler', 5, 2, 'p', state=('nomoves',))
    s.unit('trireme', 7, 5, 'p', cargo=2)
    s.unit('cavalry', 4, 5, 'p', select='yellow')
    save(s, 'roman-ancient', 'cavalry', (4, 5), 'ukiyo-e-R-portrait')


def roman_vs_asian():
    s = Scene(True)
    irrigation(s.canvas, [(4, 7)])
    roads(s.canvas, [[(3, 6), (4, 6), (5, 6)], [(5, 3), (6, 2), (7, 1)]])
    s.obj('fish', 8, 5, fit=0.5)
    s.obj('horses', 6, 0, fit=0.5)
    fog(s.canvas, [(0, 0), (1, 0), (0, 1), (9, 6), (9, 7), (8, 7)])
    goto_path(s.canvas, [(3, 5), (2, 4), (2, 3)])
    # Roman player, south-west of the river
    s.city('rcity', 3, 6, 'p', size=7, capital=True)
    s.city('rtown', 5, 6, 'p', size=3)
    s.unit('legion', 4, 5, 'p', stack=3, stack_style='badge')
    s.unit('trireme', 7, 5, 'p')
    s.unit('rsettler', 4, 6, 'p')
    s.unit('cavalry', 3, 5, 'p', select='yellow')
    # Asian rival, north-east of the river
    s.city('castle', 5, 3, 'r', size=6, capital=True)
    s.city('han', 7, 1, 'r', size=3)
    s.unit('foot', 4, 3, 'r', state=('fortified',))
    s.unit('rider', 6, 2, 'r')
    s.unit('qin', 5, 2, 'r')
    s.unit('ship', 8, 3, 'r')
    save(s, 'roman-vs-asian', 'cavalry', (3, 5), 'ukiyo-e-R-portrait')


def modern():
    s = Scene(True)
    irrigation(s.canvas, [(4, 7), (5, 7)])
    roads(s.canvas, [[(3, 6), (4, 5), (4, 4), (5, 3), (6, 2)]], rail=True)
    s.obj('ukiyo-e-W-pollution', 4, 3, fit=0.62)
    s.obj('ukiyo-e-W-pollution', 5, 2, fit=0.62)
    s.obj('fish', 8, 5, fit=0.5)
    goto_path(s.canvas, [(4, 5), (3, 4), (3, 3), (4, 2), (5, 1)])
    s.city('wcity', 5, 3, 'p', size=14, capital=True, garrison=3)
    s.city('wcity2', 3, 6, 'p', size=11)
    s.city('acity', 7, 1, 'r', size=9)
    s.unit('artillery', 6, 2, 'p', state=('fortified', 'veteran'))
    s.unit('mech', 6, 1, 'p', stack=3, stack_style='badge')
    s.unit('battleship', 8, 3, 'p')
    s.unit('transport', 7, 5, 'p', cargo=3)
    s.unit('atank', 7, 0, 'r')
    s.unit('meiji', 8, 2, 'r')
    s.unit('bomber', 7, 2, 'p')
    s.unit('armor', 4, 5, 'p', select='yellow')
    save(s, 'modern', 'armor', (4, 5), 'ukiyo-e-W-portrait')


def eras():
    rows = [('Ancient', 'han', 'qin', 'rcity', 'legion'), ('Medieval', 'castle', 'rider', 'gothic', 'knight'),
            ('Industrial', 'tokyo', 'meiji', 'port', 'rifle'), ('Modern', 'acity', 'atank', 'wcity', 'armor')]
    heads = ['Asian city', 'Asian unit', 'Western city', 'Western unit']
    k, m = 1.75, 16
    pw = round((CELL + 2 * m) * k)
    lw, top, pad = 190, 56, 16
    img = Image.new('RGB', (lw + 4 * (pw + pad) + pad, top + 4 * (pw + pad) + pad), (240, 229, 202))
    d = ImageDraw.Draw(img)
    font = ImageFont.truetype(SANS, 28)
    for j, h in enumerate(heads):
        d.text((lw + pad + j * (pw + pad) + 8, 14), h, fill=INK, font=font)
    c, r = 4, 6
    for i, (era, *names) in enumerate(rows):
        y = top + i * (pw + pad)
        d.text((16, y + pw // 2 - 16), era, fill=INK, font=font)
        for j, n in enumerate(names):
            s = Scene(True)
            if j % 2 == 0:
                s.city(n, c, r, 'p', size=[3, 6, 9, 14][i])
            else:
                s.unit(n, c, r, 'p')
            s.check()
            crop = comp2.with_grid(s.canvas).convert('RGB').crop((c * CELL - m, r * CELL - m, (c + 1) * CELL + m, (r + 1) * CELL + m))
            x = lw + pad + j * (pw + pad)
            img.paste(crop.resize((pw, pw), Image.LANCZOS), (x, y))
            d.rectangle((x - 2, y - 2, x + pw + 1, y + pw + 1), outline=INK, width=2)
    img.save(f'{OUT}/eras-sheet.jpg', quality=92)


if __name__ == '__main__':
    for w in sys.argv[1:] or ['roman_ancient', 'roman_vs_asian', 'modern', 'eras']:
        globals()[w]()
