"""One game-screenshot plate per graphic and cartographic style."""

import base64
import io
import json
import os
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed

from PIL import Image, ImageDraw, ImageFont

OUT = "/opt/cursor/artifacts/moodboard/screens"
os.makedirs(OUT, exist_ok=True)

SCENE = (
    "A widescreen screenshot of a turn-based strategy game. The left quarter is an interface column: "
    "a small world-map rectangle, a portrait box of the selected unit, and a few plain bars where text would sit. "
    "No readable words, letters, or numbers anywhere. "
    "The remaining screen is the play map, seen straight down, cut into a clear grid of square cells, about twelve across and eight down. "
    "Cells are grassland, a patch of forest, hills, a coastline, and ocean. "
    "A walled city with a temple occupies one grassland cell. A knight on a horse occupies another land cell, and that cell is marked with a bright selection rectangle. "
    "A short road joins two cells. Cells along the far edge are darker, as unexplored territory. "
)

STYLES = [
    ("01-pixel", "Chunky pixel",
     "The entire screenshot, panel and map, is chunky pixel art. Every shape is a hard square of one flat color, large enough to count, with no anti-aliasing and no blur. "
     "A fixed palette of dark green, light green, olive, brown, blue, grey, red, black, and cream. "
     "Each map cell is a tile of those squares. The knight is a sprite. The city is a sprite. The left panel is the same pixels."),
    ("02-blueprint", "Blueprint",
     "The entire screenshot is a blueprint on flat Prussian-blue paper. Thin white lines of one weight only. "
     "Map cells are squares of contour lines. Forest is a repeated tree symbol. The city is a small plan. The knight is a tiny elevation symbol inside the selected square. "
     "The left panel is a white-line diagram. Most of each cell is empty blue. No shading, no painted figures."),
    ("03-sketch", "Survey sketch",
     "The entire screenshot is a survey sketch in brown ink on cream paper. One nib. Construction lines remain and paper shows through. "
     "Each map cell is a lightly ruled square. Grass is a few pen flicks. The city and the knight are small open line drawings sitting in their cells. "
     "The left panel is ruled boxes. No finished painting underneath."),
    ("04-topo", "Topographic",
     "The play map is an Ordnance Survey sheet used as a game board. White paper, pale green land, brown contours, blue water, one red road along the grid. "
     "The city is a black built-up stamp inside its cell. The knight is a small black game token inside the selected cell. "
     "The left panel is printed map furniture: a neat rectangle and a few rules, with no words. Flat print, no perspective."),
    ("05-portolan", "Portolan chart",
     "The play map is a portolan chart used as a game board. Vellum ground, rhumb lines crossing the ocean cells, two small compass roses as interface ornaments. "
     "Land cells are empty vellum with a dark coast outline. The city is a tiny red castle token in a coastal cell. The knight is a small token in the selected square. "
     "The left panel is drawn in the same ink. Lots of empty vellum. No modern satellite colors."),
    ("06-hydro", "Hydrographic",
     "The play map is a modern nautical chart used as a game board. Pale blue water cells with tiny sounding marks, tan land cells, a black coastline. "
     "The city is a harbor-town symbol in a coastal cell. The knight is a small token in the selected cell. Buoy marks in the water cells. "
     "The left panel matches the chart. Flat, overhead, no painted waves."),
    ("07-atlas", "Dutch atlas",
     "The play map is a 17th-century Dutch atlas page used as a game board. Fine engraved line, pale green fields, a blue river, tiny molehill hills, the city a red plan inside its cell. "
     "The knight is a tiny engraved horseman in the selected cell. Grid lines are fine engraved rules. "
     "The left panel is an empty ornamental cartouche used as the interface, with no letters. Flat map, no perspective."),
    ("08-pictorial", "Pictorial map",
     "The play map is a 1930s pictorial map used as a game board. Cream paper, flat color, a thick even outline. "
     "Trees, hills, and the walled town are small drawings sitting inside the square cells. The knight is a small figure piece in the selected cell. "
     "The grid stays visible. The left panel is a simple framed box. No title, no place names, no sentences."),
]


def font(size):
    for path in ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                 "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf"):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def generate(key, slug, title, style):
    path = os.path.join(OUT, slug + ".jpg")
    prompt = SCENE + style
    last = ""
    for size in ("1536x1024", "1024x1024"):
        body = json.dumps({
            "model": "gpt-image-2", "prompt": prompt, "size": size, "quality": "low", "n": 1,
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
            last = f"HTTP {e.code} {text[:200]}"
            print(f"FAIL {slug} {size} {last}", flush=True)
            fatal = e.code in (401, 402, 403, 429) or any(
                w in text.lower() for w in ("billing", "quota", "insufficient", "budget"))
            if fatal or "size" not in text.lower():
                return None, last, fatal
            continue
        img = Image.open(io.BytesIO(base64.b64decode(payload["data"][0]["b64_json"]))).convert("RGB")
        img.save(path, quality=90)
        print(f"OK {slug} {size} {img.size}", flush=True)
        return path, title, False
    return None, last, False


def board(plates):
    thumb_w, thumb_h, cap, cols = 640, 426, 32, 2
    rows = (len(plates) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * thumb_w, rows * (thumb_h + cap)), (24, 24, 24))
    draw = ImageDraw.Draw(sheet)
    label = font(18)
    for i, (path, title) in enumerate(plates):
        im = Image.open(path).convert("RGB")
        im.thumbnail((thumb_w, thumb_h), Image.LANCZOS)
        x, y = (i % cols) * thumb_w, (i // cols) * (thumb_h + cap)
        sheet.paste(im, (x, y))
        draw.text((x + 12, y + im.height + 6), title, fill=(240, 236, 228), font=label)
    out = "/opt/cursor/artifacts/moodboard/screen-board.jpg"
    sheet.save(out, quality=90)
    print("BOARD", out, sheet.size, flush=True)


def main():
    key = os.environ["OPENAI_API_KEY"]
    plates = []
    with ThreadPoolExecutor(4) as pool:
        futs = [pool.submit(generate, key, s, t, p) for s, t, p in STYLES]
        for fut in as_completed(futs):
            path, info, fatal = fut.result()
            if path is None:
                print("MISS", info, flush=True)
                if fatal:
                    break
                continue
            plates.append((path, info))
    order = {s[0] + ".jpg": i for i, s in enumerate(STYLES)}
    plates.sort(key=lambda item: order.get(os.path.basename(item[0]), 99))
    if plates:
        board(plates)
    print(f"DONE {len(plates)} of {len(STYLES)}", flush=True)


if __name__ == "__main__":
    main()
