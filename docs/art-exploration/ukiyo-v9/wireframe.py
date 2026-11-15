"""Blueprint wireframe of the panorama city screen at the game's HD size (1280x800, 4x the original 320x200)."""
from PIL import Image, ImageDraw, ImageFont
S = 2                      # drawn at 2x for legibility: 2560x1600
W, H = 1280, 800
BG, GRID, LINE, DIM, ACC = (16, 42, 82), (30, 62, 108), (225, 236, 250), (140, 175, 215), (255, 196, 120)
im = Image.new('RGB', (W * S, H * S + 260 * S), BG); d = ImageDraw.Draw(im)
def font(sz, bold=False):
    for n in (('arialbd.ttf' if bold else 'arial.ttf'), 'DejaVuSans.ttf'):
        try: return ImageFont.truetype(n, sz * S)
        except OSError: pass
    return ImageFont.load_default()
F, FS, FB, FT = font(13), font(11), font(15, True), font(20, True)
for x in range(0, W + 1, 16): d.line([(x * S, 0), (x * S, H * S)], fill=GRID, width=1)
for y in range(0, H + 1, 16): d.line([(0, y * S), (W * S, y * S)], fill=GRID, width=1)
d.rectangle([0, 0, W * S - 1, H * S - 1], outline=LINE, width=2 * S)
def box(x1, y1, x2, y2, title, lines=(), dash=False, civ=None):
    r = [x1 * S, y1 * S, x2 * S, y2 * S]
    if dash:
        for x in range(x1, x2, 12): d.line([(x * S, y1 * S), (min(x + 6, x2) * S, y1 * S)], fill=LINE, width=S); d.line([(x * S, y2 * S), (min(x + 6, x2) * S, y2 * S)], fill=LINE, width=S)
        for y in range(y1, y2, 12): d.line([(x1 * S, y * S), (x1 * S, min(y + 6, y2) * S)], fill=LINE, width=S); d.line([(x2 * S, y * S), (x2 * S, min(y + 6, y2) * S)], fill=LINE, width=S)
    else:
        d.rectangle(r, outline=LINE, width=S)
    d.text(((x1 + 8) * S, (y1 + 6) * S), title, fill=LINE, font=FB)
    for i, t in enumerate(lines): d.text(((x1 + 8) * S, (y1 + 28 + i * 17) * S), t, fill=DIM, font=F)
    d.text(((x2 - 6) * S, (y2 - 5) * S), f'{x2 - x1}x{y2 - y1}', fill=DIM, font=FS, anchor='rd')
    if civ and y2 - y1 < 70 and x2 - x1 > 600: d.text(((x2 - 90) * S, (y2 - 5) * S), 'civ1: ' + civ, fill=ACC, font=FS, anchor='rd')
    elif civ: d.text(((x1 + 8) * S, (y2 - 5) * S), 'civ1: ' + civ, fill=ACC, font=FS, anchor='ld')
# frame
box(0, 0, 1280, 40, 'TITLE BAR   < BABYLON (8) >   1250 BC   Treasury 340', civ='city name line')
box(0, 40, 1280, 96, 'CITIZENS', ['happy | content | unhappy | specialists (click a specialist: cycle entertainer > taxman > scientist)'], civ='citizen row')
box(16, 104, 300, 404, 'CITY RESOURCES', ['food row, surplus drawn pale', 'shields row, waste struck out', 'trade row, corruption struck out', 'luxury | tax | science split', '', 'icons 24x24, one size everywhere'], civ='City Resources box')
box(16, 412, 300, 640, 'CITY MAP', ['21 squares, 40x40 each', 'click square: work / free citizen', 'hover: food/shield/trade yield'], civ='city map')
box(312, 104, 968, 640, 'CITY VIEW  (byobu painting, always visible)', ['isometric 2:1, fixed plot per improvement, houses fill the rest', 'gold clouds hide unbuilt edges; city grows into them', 'hover building: outline + name label', 'click building: info card (upkeep, effect, SELL)', 'click scaffold: production panel', 'wonders take reserved plots outside the wall ring', 'celebration / disorder / attack play here as overlays'], civ='VIEW screen (now the centre)')
for i, n in enumerate(('Palace', 'Granary', 'Temple', 'Barracks', 'City Walls', 'Library', 'Marketplace', 'Aqueduct')):
    y = 140 + i * 52; d.rectangle([988 * S, y * S, 1256 * S, (y + 46) * S], outline=DIM, width=S)
    d.rectangle([994 * S, (y + 7) * S, 1026 * S, (y + 39) * S], outline=DIM, width=S)
    d.text((1036 * S, (y + 23) * S), n, fill=DIM, font=F, anchor='lm')
box(980, 104, 1264, 640, 'IMPROVEMENTS', [], civ='improvement list')
d.text((1122 * S, 604 * S), 'MORE   |   WONDERS', fill=LINE, font=F, anchor='mm')
box(16, 652, 440, 784, 'UNITS', ['In City: 6 slots 48x48  (click: activate / fortify)', 'Supported: 6 slots  (click: disband)'], civ='Units Present + support')
box(452, 652, 700, 784, 'FOOD STORAGE', ['basket fills by rows', 'turns to growth'], civ='Food Storage')
box(712, 652, 1120, 784, 'PRODUCTION', ['item picture | name | shields 14/20 | turns', '[CHANGE]  [BUY 52g]  [AUTO]'], civ='production box')
box(1132, 652, 1264, 712, 'RENAME', civ='RENAME')
box(1132, 720, 1264, 784, 'EXIT', civ='EXIT')
# legend
y0 = H * S + 30 * S
d.text((24 * S, y0), 'OpenCivOne city screen, wireframe v9   1280 x 800 (4x the 320x200 original), 16 px grid', fill=LINE, font=FT)
notes = ['Every Civ1 city-screen element keeps its place in reading order: citizens on top, resources and map left, buildings right, units / food / production bottom.',
         'The one change: the VIEW screen moves into the centre and is always shown (Heroes 3 town-screen idea). INFO / HAPPY / MAP / VIEW tabs are no longer needed:',
         '  HAPPY becomes a hover on the citizen row, MAP is the city map panel, VIEW is the centre, INFO is the units panel.',
         'Panels: black-lacquer frame, gold-leaf ground, title on a paper strip. Gutters 8-12 px. Text never smaller than 13 px at 1280x800.',
         'Orange labels: the original Civ1 element each rectangle replaces.']
for i, n in enumerate(notes): d.text((24 * S, y0 + (40 + i * 30) * S), n, fill=DIM, font=F)
im.save('wireframe.jpg'); print('saved', im.size)
