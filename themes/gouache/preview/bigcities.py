"""Shows big-city variants (raw-big/*.png) in Babylon's cell of the engine preview, at game scale and zoomed, on one board.
Usage: python bigcities.py ENGINE_PNG OUT_JPG"""
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont
sys.path.insert(0, '../../../tools/hdart')
from painted_assets import cut_object

CELL, X, Y = 64, 6, 4
VARIANTS = [('big_a_landmarks', 'A  tall landmarks, inside the square', False),
            ('big_b_skyline', 'B  skyline rising above the square', True),
            ('big_c_marble', 'C  marble and gold capital', False),
            ('big_d_ziggurat', 'D  ziggurat city', False),
            ('big_e_player', 'E  player-colour roofs and banners', False),
            ('big_f_grand', 'F  grand walled capital', False)]


def soft(t):
    """Keyed town: magenta spill to earth, edge pulled in and feathered, contrast raised (as compose.py)."""
    a = np.asarray(t.convert('RGBA'), np.float32)
    rgb, alpha = a[..., :3], a[..., 3]
    pink = (rgb[..., 0] - rgb[..., 1] > 25) & (rgb[..., 2] - rgb[..., 1] > 25)
    rgb[pink] = np.median(rgb[(alpha > 250) & ~pink], axis=0)
    edge = Image.fromarray(alpha.astype('uint8')).filter(ImageFilter.MinFilter(5)).filter(ImageFilter.GaussianBlur(3))
    alpha = np.minimum(alpha, np.asarray(edge, np.float32))
    rgb = (rgb - 110) * 1.2 + 110
    return Image.fromarray(np.dstack([rgb, alpha]).clip(0, 255).astype('uint8'), 'RGBA')


def font(size):
    try:
        return ImageFont.truetype('georgiab.ttf', size)
    except OSError:
        return ImageFont.load_default()


def place(base, town, tall):
    im = base.copy()
    t = town
    f = (CELL * 0.98 / t.width) if tall else (CELL * 0.98 / max(t.size))
    t = t.resize((round(t.width * f), round(t.height * f)), Image.LANCZOS)
    # a skyline stands on the cell's bottom edge and rises above it; the others are centred in the cell
    left = round(X * CELL + (CELL - t.width) / 2)
    top = round((Y + 1) * CELL - t.height - 1) if tall else round(Y * CELL + (CELL - t.height) / 2)
    im.alpha_composite(t, (left, top))
    d = ImageDraw.Draw(im)
    bx, by = X * CELL + 1, Y * CELL + 1
    d.rectangle([bx, by, bx + 13, by + 13], fill=(48, 88, 190), outline=(20, 18, 16))
    d.text((bx + 7, by + 7), '12', font=font(9), fill='white', anchor='mm')
    for o in (2, 1):
        d.text((X * CELL + 32 + o, (Y + 1) * CELL + o), 'Babylon', font=font(12), fill=(20, 18, 16), anchor='mt')
    d.text((X * CELL + 32, (Y + 1) * CELL), 'Babylon', font=font(12), fill=(255, 250, 235), anchor='mt')
    return im


if __name__ == '__main__':
    base = Image.open(sys.argv[1]).convert('RGBA')
    crop = ((X - 2) * CELL, (Y - 1) * CELL, (X + 3) * CELL, (Y + 3) * CELL)
    tiles = []
    for name, label, tall in VARIANTS:
        try:
            town = soft(cut_object(Image.open(f'../raw-big/{name}.png'), size=512))
        except FileNotFoundError:
            continue
        im = place(base, town, tall).crop(crop).convert('RGB')
        game = im
        zoom = im.resize((im.width * 2, im.height * 2), Image.LANCZOS)
        tile = Image.new('RGB', (game.width + zoom.width + 30, zoom.height + 40), (28, 26, 24))
        tile.paste(game, (10, 40))
        tile.paste(zoom, (game.width + 20, 40))
        ImageDraw.Draw(tile).text((10, 12), label + '   (left: game size, right: 2x)', font=font(18), fill=(240, 232, 214))
        tiles.append(tile)
    w, h = tiles[0].size
    board = Image.new('RGB', (w * 2, h * ((len(tiles) + 1) // 2)), (28, 26, 24))
    for i, t in enumerate(tiles):
        board.paste(t, ((i % 2) * w, (i // 2) * h))
    board.save(sys.argv[2], quality=90)
