"""Builds the conquest screen pictures for a theme (see themes/<theme>/slam/prompts.json).

SLAM1: the hall behind the winner, a painted throne hall with fourteen empty frames on the wall, two rows of seven,
where the game puts the heads of the fallen rulers (27x33 original pixels each at x = 8 + 46 * column, y = 49 for
the first row and 7 for the second). SLAM2: those fourteen heads, cut from the theme's leader busts (KING00-13, in
the same order), laid out as the game reads them: at (28 * (i % 7) + 1, 34 * (i // 7)), 27x33 each.

Usage: python3 slam.py RAW_HALL.png THEME_HD_DIR
"""
import glob
import sys

from PIL import Image, ImageDraw, ImageFilter

S = 4
HEAD = (27, 33)


def hall(raw, hd):
    img = Image.open(raw).convert('RGB')
    w = img.width
    h = w * 10 // 16
    top = int(img.height * 0.2)
    img = img.crop((0, top, w, top + h)).resize((320 * S, 200 * S), Image.LANCZOS).convert('RGBA')
    shade = Image.new('RGBA', img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(shade)
    for k in range(14):
        x, y = 8 + 46 * (k % 7), (49 if k < 7 else 7)
        box = ((x - 2) * S, (y - 2) * S, (x + HEAD[0] + 2) * S, (y + HEAD[1] + 2) * S)
        d.rectangle((box[0] + 6, box[1] + 8, box[2] + 6, box[3] + 8), fill=(0, 0, 0, 120))   # its shadow
    img.alpha_composite(shade.filter(ImageFilter.GaussianBlur(5)))
    d = ImageDraw.Draw(img)
    for k in range(14):
        x, y = 8 + 46 * (k % 7), (49 if k < 7 else 7)
        box = ((x - 2) * S, (y - 2) * S, (x + HEAD[0] + 2) * S - 1, (y + HEAD[1] + 2) * S - 1)
        d.rectangle(box, fill=(52, 38, 26))                                       # the frame's dark key line
        d.rectangle((box[0] + 2, box[1] + 2, box[2] - 2, box[3] - 2), fill=(176, 138, 70))   # gilt
        d.rectangle((box[0] + 5, box[1] + 5, box[2] - 5, box[3] - 5), fill=(120, 88, 44))
        d.rectangle((x * S - 1, y * S - 1, (x + HEAD[0]) * S, (y + HEAD[1]) * S), fill=(30, 24, 20))  # empty canvas
    img.convert('RGB').save(f'{hd}/SLAM1.jpg', quality=92)


def heads(hd):
    sheet = Image.new('RGB', (320 * S, 200 * S), (0, 0, 0))
    for i in range(14):
        bust = Image.open(sorted(glob.glob(f'{hd}/KING{i:02}/181_67*.png'))[0]).convert('RGBA')
        w, h = bust.size
        ch = int(h * 0.78)
        cw = ch * HEAD[0] // HEAD[1]
        crop = bust.crop(((w - cw) // 2, 0, (w + cw) // 2, ch)).resize((HEAD[0] * S, HEAD[1] * S), Image.LANCZOS)
        cell = Image.new('RGBA', crop.size, (58, 44, 34, 255))
        cell.alpha_composite(crop)
        sheet.paste(cell.convert('RGB'), ((28 * (i % 7) + 1) * S, (34 * (i // 7)) * S))
    sheet.save(f'{hd}/SLAM2.png')


if __name__ == '__main__':
    hall(sys.argv[1], sys.argv[2])
    heads(sys.argv[2])
    print('conquest screen written')
