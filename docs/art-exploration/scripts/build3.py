import sys
from PIL import Image, ImageDraw, ImageFont
import comp2
from states import Scene, OUT, CELL, SANS, INK, MW, MH

SUFFIX = {False: '', True: '-outline'}


def merged(outline):
    s = Scene(outline)
    s.city('castle', 5, 3, 'p', size=8)
    s.city('town', 3, 6, 'p', size=4)
    s.city('town', 7, 1, 'r', size=3)
    s.unit('ship', 7, 5, 'p')
    s.unit('rider', 4, 5, 'p', select='yellow')
    s.unit('settler', 5, 6, 'p')
    s.unit('foot', 6, 1, 'r')
    s.unit('foot', 1, 5, 'p')
    s.screen().save(f'{OUT}/units-merged{SUFFIX[outline]}.jpg', quality=93)
    return s


# ---------- the in-game moment ----------
FOG = [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (0, 2), (0, 3), (9, 6), (9, 7), (8, 7)]


def scene(outline):
    s = Scene(outline)
    from states import irrigation, roads, fog, goto_path
    irrigation(s.canvas, [(5, 2), (4, 3)])
    roads(s.canvas, [[(5, 3), (4, 4), (4, 5), (3, 6)], [(5, 3), (6, 2)]])
    s.obj('horses', 4, 7, fit=0.5)
    s.obj('fish', 8, 4, fit=0.5)
    s.obj('hut', 1, 3, fit=0.5)
    s.obj('mine', 0, 5, fit=0.52)
    fog(s.canvas, FOG)
    goto_path(s.canvas, [(4, 5), (3, 4), (2, 3), (2, 2)])
    s.city('castle', 5, 3, 'p', size=8, walls=True, capital=True, garrison=2)
    s.city('town', 3, 6, 'p', size=4, disorder=True)
    s.city('town', 7, 1, 'r', size=3)
    s.unit('foot', 6, 2, 'p', state=('fortified', 'veteran'))
    s.unit('foot', 7, 0, 'r')
    s.unit('rider', 8, 2, 'r', state=())
    s.unit('foot', 4, 6, 'p', stack=3, stack_style='badge')
    s.unit('settler', 5, 2, 'p', state=('nomoves',))
    s.unit('ship', 7, 5, 'p', cargo=2)
    s.unit('rider', 4, 5, 'p', select='yellow')
    s.screen().save(f'{OUT}/scene{SUFFIX[outline]}.jpg', quality=93)
    s.screen(grid=True).save(f'{OUT}/scene{SUFFIX[outline]}-grid.jpg', quality=93)
    return s


if __name__ == '__main__':
    what = sys.argv[1:] or ['merged', 'scene']
    for o in (False, True):
        if 'merged' in what:
            merged(o)
        if 'scene' in what:
            scene(o)


# ---------- reference sheet ----------
def P(label, region, fn):
    return label, region, fn


def panels():
    from states import irrigation, roads, fog, goto_path
    sel = lambda style: (lambda s: (s.unit('rider', 4, 5, select=style), s.unit('foot', 5, 6)))
    return [
        P('Selected: yellow square', (3, 5), sel('yellow')),
        P('Selected: cinnabar brush', (3, 5), sel('brush')),
        P('Selected: cream glow', (3, 5), sel('glow')),
        P('Stack: offset copies (3)', (3, 5), lambda s: (s.unit('foot', 4, 5, stack=3, stack_style='offset'), s.unit('settler', 5, 6))),
        P('Stack: count seal (3)', (3, 5), lambda s: (s.unit('foot', 4, 5, stack=3, stack_style='badge'), s.unit('settler', 5, 6))),
        P('Stack: copy + seal (3)', (3, 5), lambda s: (s.unit('foot', 4, 5, stack=3, stack_style='both'), s.unit('settler', 5, 6))),
        P('Fortified (right) vs normal', (4, 2), lambda s: (s.unit('foot', 6, 2), s.unit('foot', 5, 2, state=('fortified',)))),
        P('Sentry (right) vs normal', (3, 5), lambda s: (s.unit('foot', 4, 5), s.unit('foot', 5, 5, state=('sentry',)))),
        P('No moves left (right)', (3, 5), lambda s: (s.unit('rider', 4, 5), s.unit('rider', 5, 5, state=('nomoves',)))),
        P('Veteran (right)', (3, 5), lambda s: (s.unit('foot', 4, 5), s.unit('foot', 5, 5, state=('veteran',)))),
        P('City size', (3, 5), lambda s: (s.city('town', 3, 6, size=3), s.unit('rider', 4, 5))),
        P('City with walls', (4, 2), lambda s: s.city('town', 5, 3, size=5, walls=True)),
        P('Capital', (4, 2), lambda s: s.city('castle', 5, 3, size=8, capital=True)),
        P('Garrison: 2 units inside', (3, 5), lambda s: s.city('town', 3, 6, size=4, garrison=2)),
        P('City in disorder', (3, 5), lambda s: s.city('town', 3, 6, size=4, disorder=True)),
        P('Road and railroad', (3, 5), lambda s: (roads(s.canvas, [[(2, 4), (3, 5), (4, 5), (5, 4)]]), roads(s.canvas, [[(2, 6), (3, 6), (4, 6), (5, 6), (6, 7)]], rail=True))),
        P('Irrigation', (3, 4), lambda s: irrigation(s.canvas, [(3, 5), (4, 5), (5, 5)])),
        P('Mine (hills) and hut', (0, 3), lambda s: (s.obj('mine', 0, 3, fit=0.52), s.obj('hut', 1, 4, fit=0.5))),
        P('Resources: horses, fish', (5, 5), lambda s: (s.obj('horses', 5, 6, fit=0.5), s.obj('fish', 7, 5, fit=0.5))),
        P('Fog of war', (1, 1), lambda s: (fog(s.canvas, [(0, 0), (1, 0), (2, 0), (3, 0), (0, 1), (1, 1), (0, 2)]), s.unit('rider', 2, 2))),
        P('Combat', (5, 1), lambda s: (s.city('town', 7, 1, 'r', size=3), s.unit('foot', 6, 1, 'r'), s.burst(6, 1), s.unit('rider', 5, 2))),
        P('Ship carrying 3', (7, 4), lambda s: s.unit('ship', 7, 5, cargo=3)),
        P('Go-to path', (3, 4), lambda s: (goto_path(s.canvas, [(5, 5), (4, 4), (3, 4)]), s.unit('rider', 5, 5, select='yellow'))),
        P('Cream outline: off | on', (3, 5), lambda s: (s.unit('settler', 4, 5, outline=False), s.unit('settler', 5, 5, outline=True))),
    ]


def sheet(outline, k=1.0):
    pw, ph = round(3 * CELL * k), round(2 * CELL * k)
    cols, pad, lab = 4, 18, 40
    ps = panels()
    rows = (len(ps) + cols - 1) // cols
    img = Image.new('RGB', (cols * pw + (cols + 1) * pad, rows * (ph + lab + pad) + pad), (240, 229, 202))
    d = ImageDraw.Draw(img)
    font = ImageFont.truetype(SANS, 24)
    for i, (label, (c0, r0), fn) in enumerate(ps):
        s = Scene(outline)
        fn(s)
        s.check()
        crop = s.canvas.convert('RGB').crop((c0 * CELL, r0 * CELL, (c0 + 3) * CELL, (r0 + 2) * CELL))
        if k != 1:
            crop = crop.resize((pw, ph), Image.LANCZOS)
        x = pad + (i % cols) * (pw + pad)
        y = pad + (i // cols) * (ph + lab + pad)
        d.text((x, y + 4), label, fill=INK, font=font)
        img.paste(crop, (x, y + lab))
        d.rectangle((x - 2, y + lab - 2, x + pw + 1, y + lab + ph + 1), outline=INK, width=2)
    img.save(f'{OUT}/states-sheet{SUFFIX[outline]}.jpg', quality=92)


def stacks(outline, k=2.0):
    """The same cell, 2x: single unit and the stack treatments, plus the count seal on a forest cell, a ship and a city."""
    pad, lab = 18, 40
    m = 20
    cases = [
        ('Single unit', (4, 5), lambda s: s.unit('foot', 4, 5)),
        ('Civ1: one copy behind', (4, 5), lambda s: s.unit('foot', 4, 5, stack=2, stack_style='both') if False else civ1(s)),
        ('Offset copies (3)', (4, 5), lambda s: s.unit('foot', 4, 5, stack=3, stack_style='offset')),
        ('Count seal (3)', (4, 5), lambda s: s.unit('foot', 4, 5, stack=3, stack_style='badge')),
        ('Copy + seal (3)', (4, 5), lambda s: s.unit('foot', 4, 5, stack=3, stack_style='both')),
        ('Seal on forest (5)', (1, 5), lambda s: s.unit('foot', 1, 5, stack=5, stack_style='badge')),
        ('Ship carrying 3', (7, 5), lambda s: s.unit('ship', 7, 5, cargo=3)),
        ('City garrison 2', (3, 6), lambda s: s.city('town', 3, 6, size=4, garrison=2)),
    ]
    pw = round((CELL + 2 * m) * k)
    cols = 4
    img = Image.new('RGB', (cols * pw + (cols + 1) * pad, 2 * (pw + lab + pad) + pad), (240, 229, 202))
    d = ImageDraw.Draw(img)
    font = ImageFont.truetype(SANS, 24)
    for i, (label, (c, r), fn) in enumerate(cases):
        s = Scene(outline)
        fn(s)
        s.check()
        g = comp2.with_grid(s.canvas).convert('RGB')
        crop = g.crop((c * CELL - m, r * CELL - m, (c + 1) * CELL + m, (r + 1) * CELL + m)).resize((pw, pw), Image.LANCZOS)
        x = pad + (i % cols) * (pw + pad)
        y = pad + (i // cols) * (pw + lab + pad)
        d.text((x, y + 4), label, fill=INK, font=font)
        img.paste(crop, (x, y + lab))
        d.rectangle((x - 2, y + lab - 2, x + pw + 1, y + lab + pw + 1), outline=INK, width=2)
    img.save(f'{OUT}/stacks{SUFFIX[outline]}.jpg', quality=92)


def civ1(s):
    s.unit('foot', 4, 5, stack=2, stack_style='offset')


if __name__ == '__main__' and 'sheets' in sys.argv:
    for o in (False, True):
        sheet(o)
        stacks(o)
