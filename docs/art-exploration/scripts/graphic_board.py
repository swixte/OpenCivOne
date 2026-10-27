"""One plate each for the graphic styles and the cartographic fields. Key comes from the environment."""

import base64
import io
import json
import os
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed

from PIL import Image, ImageDraw, ImageFont

OUT = "/opt/cursor/artifacts/moodboard/graphic"
os.makedirs(OUT, exist_ok=True)

STYLES = [
    ("01-pixel", "Chunky pixel",
     "A chunky pixel-art image with perfect hard squares and no anti-aliasing. Every square is one flat color, about 28 image-pixels wide, aligned to a grid. "
     "Palette only: dark green, light green, olive, brown, tan, grey, blue, red, black, cream. "
     "A patch of grassland made of those squares, a small walled city of squares with one temple, and a knight on a horse standing on the grass, also made only of those squares. "
     "No blur, no gradients, no texture inside a square, no outlines softer than one square, no painting."),
    ("02-blueprint", "Blueprint",
     "A real architectural blueprint. The paper is one flat Prussian blue with no texture and no vignette. Drawing is thin white lines of a single weight, plus dashed lines and a little hatch. Most of the sheet is empty blue. "
     "Left: an overhead plan of a small walled city and one temple, with contour lines for the grassland around it. Right: an elevation of a horse and rider drawn in the same white line, like a machine drawing. "
     "A blank title block made of lines only, with no letters. No shading, no color fills, no realistic body, no photograph, no painted knight."),
    ("03-sketch", "Survey sketch",
     "A surveyor's field sketch in brown ink on warm cream paper. One nib. Faint construction lines remain and corners are left open, so bare paper shows through. "
     "Grass is a few repeated pen flicks with empty paper between them. A walled city and a columned temple are a quick measured sketch. A horse and rider are built from the same open strokes. "
     "No pencil filter, no finished picture underneath, no watercolor, no grey smudge."),
    ("04-topo", "Topographic",
     "A printed topographic map seen straight down, like an Ordnance Survey sheet. White paper. Pale green land, brown contour lines, one blue stream, one red road. "
     "The walled city is a small black built-up patch. The temple is a tiny black square symbol. The rider is a tiny black map symbol on the road, not a drawn person. "
     "A north arrow and a scale bar with no readable words. Flat ink, no perspective, no landscape painting, no illustration of hills."),
    ("05-portolan", "Portolan chart",
     "A medieval portolan chart on blank vellum. Straight rhumb lines in black, red and green cross the sheet from two compass roses. "
     "The land is only a dark ink outline of a coast and a patch of grassland, with the interior of the vellum left empty. "
     "A walled city is a small red-and-gold castle drawn on the coastline. No modern colors, no satellite view, no perspective, no readable names."),
    ("06-hydro", "Hydrographic",
     "A modern nautical chart, printed flat and seen straight down. Pale blue water with small depth marks, a tan shore, the grassland as low land behind a coastline. "
     "The walled city is a dense black town symbol on the coast. The temple is a landmark: a small circle with a dot. A compass rose. "
     "Buoy marks in the water. No painted waves, no picture of a ship in perspective, no readable words."),
    ("07-atlas", "Dutch atlas",
     "A 17th-century Dutch copperplate atlas map, fine engraved line with delicate hand color: pale green land, a blue river, the city washed pale red. "
     "Hills are tiny engraved molehills. The walled city is a small red plan. An empty ornamental cartouche and a compass rose, with no letters. "
     "A tiny engraved rider on a road. Overhead map, flat, no perspective painting."),
    ("08-pictorial", "Pictorial map",
     "A 1930s pictorial map on cream paper, seen from above, with small friendly drawings sitting on the map. Flat muted colors and a thick even outline. "
     "Grassland is a pale green field with one repeated tuft. The walled city is a cluster of tiny white buildings with one columned temple. "
     "A knight on a horse is a small figure on the road, about the size of a building. A simple compass. No realism, no perspective panorama."),
]


def font(size):
    for path in ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                 "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf"):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def one(key, slug, title, prompt):
    path = os.path.join(OUT, slug + ".png")
    body = json.dumps({
        "model": "gpt-image-2", "prompt": prompt, "size": "1024x1024", "quality": "low", "n": 1,
    }).encode()
    req = urllib.request.Request(
        "https://api.openai.com/v1/images/generations",
        data=body,
        headers={"Authorization": f"Bearer {key}", "Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=300) as resp:
            payload = json.loads(resp.read().decode())
    except urllib.error.HTTPError as e:
        text = e.read().decode(errors="replace")
        print(f"FAIL {slug} HTTP {e.code} {text[:220]}", flush=True)
        fatal = e.code in (401, 402, 403, 429) or any(w in text.lower() for w in ("billing", "quota", "insufficient", "budget"))
        return None, text[:180], fatal
    img = Image.open(io.BytesIO(base64.b64decode(payload["data"][0]["b64_json"]))).convert("RGB")
    img.save(path)
    print(f"OK {slug}", flush=True)
    return path, title, False


def board(plates):
    thumb, cap, cols = 512, 36, 4
    rows = (len(plates) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * thumb, rows * (thumb + cap)), (24, 24, 24))
    draw = ImageDraw.Draw(sheet)
    label = font(18)
    for i, (path, title) in enumerate(plates):
        im = Image.open(path).convert("RGB")
        im.thumbnail((thumb, thumb), Image.LANCZOS)
        x, y = (i % cols) * thumb, (i // cols) * (thumb + cap)
        sheet.paste(im, (x + (thumb - im.width) // 2, y))
        draw.text((x + 12, y + thumb + 8), title, fill=(240, 236, 228), font=label)
    out = "/opt/cursor/artifacts/moodboard/graphic-board.jpg"
    sheet.save(out, quality=90)
    print(f"BOARD {out}", flush=True)


def main():
    key = os.environ["OPENAI_API_KEY"]
    plates = []
    with ThreadPoolExecutor(4) as pool:
        futs = [pool.submit(one, key, s, t, p) for s, t, p in STYLES]
        for fut in as_completed(futs):
            path, info, fatal = fut.result()
            if path is None:
                print("MISS", info, flush=True)
                if fatal:
                    break
                continue
            plates.append((path, info))
    order = {s[0] + ".png": i for i, s in enumerate(STYLES)}
    plates.sort(key=lambda item: order[os.path.basename(item[0])])
    if plates:
        board(plates)
    print(f"DONE {len(plates)} of {len(STYLES)}", flush=True)


if __name__ == "__main__":
    main()
