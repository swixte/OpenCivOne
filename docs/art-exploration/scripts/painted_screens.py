"""Gameplay screenshots for the painted style board."""

import base64
import io
import json
import os
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed

from PIL import Image, ImageDraw, ImageFont

OUT = "/opt/cursor/artifacts/moodboard/painted-screens"
os.makedirs(OUT, exist_ok=True)

SCENE = (
    "A widescreen screenshot of a turn-based strategy game, the same layout in every style. "
    "The left quarter is an interface column: a small world-map rectangle, a portrait box of the selected unit, and a few plain bars. "
    "No readable words, letters, or numbers anywhere. "
    "The rest is the play map, seen straight down, cut into a clearly visible grid of square cells, about twelve across and eight down. "
    "Cells are grassland, a patch of forest, hills, a coastline, and ocean. "
    "A walled city with a temple occupies one grassland cell. A knight on a horse occupies another land cell, and that cell has a bright selection rectangle. "
    "A short road joins the city to the knight. Cells along the far edge are darker unexplored territory. "
    "The knight and the city stay small inside their own cells. This is a game screen, not a poster and not a single portrait. "
)

STYLES = [
    ("01-baroque", "Baroque",
     "Painted as 17th-century oil in the manner of Velazquez: brown glazes, soft brush, a grey-cream light, dull crimson and black. "
     "Each map cell is a small patch of that paint. The knight is a small oil figure inside one cell. The portrait box holds a small head. Canvas texture, no sunbeams, no chrome armor, no full-frame rider."),
    ("02-ukiyoe", "Ukiyo-e",
     "The whole screenshot is a Japanese woodblock print. A sure black keyline around every shape, flat unmodulated color, no cast shadows. "
     "Indigo, Prussian blue, cinnabar, pale straw, and black. Ocean cells are repeating carved waves. Forest cells are flat pine shapes. "
     "The knight, the city, the grid, and the left panel are all the same print."),
    ("03-mughal", "Mughal miniature",
     "The whole screenshot is a Mughal court miniature. Opaque watercolor and gold, flat light, no cast shadow. "
     "Malachite, lapis, saffron, cinnabar, and gold. Each grassland cell is a carpet of drawn plants. The city is a small white-and-gold palace inside its cell. "
     "The knight is a small court figure inside the selected cell. The panel is a painted border."),
    ("04-deco", "Art Deco",
     "The whole screenshot is an Art Deco relief. Streamlined shapes, one hard highlight, sunburst limited to the portrait box. "
     "Black, antique gold, teal, ivory, and a stripe of coral. Map cells are flat planes. The city and the knight share the same bevel and the same gold line. "
     "The grid is a thin gold rule."),
    ("05-babylon", "Glazed brick",
     "The whole screenshot is Neo-Babylonian glazed brick, like the Ishtar Gate. Cobalt blue brick courses form every cell and the panel. "
     "The knight, the trees, the hills, and the city are gold and turquoise relief figures raised from the brick, with white rosettes. "
     "No perspective, no realistic painting. The grid is the brick courses."),
    ("06-codex", "Mesoamerican codex",
     "The whole screenshot is a Mesoamerican codex on fig-bark. Flat Maya blue, red, and yellow. Figures in profile. "
     "Each cell is a flat band of color. The city is a glyph-like temple. The knight is a small profile figure in the selected cell. "
     "Ornamental marks may appear only as pattern. No European lighting, no readable words."),
    ("07-palekh", "Palekh lacquer",
     "The whole screenshot is a Russian Palekh lacquer miniature. Deep black ground. Figures and buildings are bright egg-tempera with gold hatching. "
     "Grassland cells are small green enamel patches. The city and the knight are jewel-like and stay inside their cells. The panel is black with a gold frame."),
    ("08-egypt", "Egyptian tomb",
     "The whole screenshot is Egyptian tomb painting. Flat mineral color: red-brown, pale gold, malachite, lapis, on a warm ground. "
     "Bodies in profile, no perspective and no cast shadow. Grass cells are a band of reeds. Ocean cells are a flat blue band. "
     "The city is a small pylon. The knight is a small chariot figure inside the selected cell. The grid is ruled in dark line."),
    ("09-shanshui", "Blue-green landscape",
     "The whole screenshot is a Song-dynasty blue-and-green landscape on silk, used as a game board. Azurite hills, malachite slopes, gold silk grassland. "
     "The grid is a faint ink rule and it stays visible. The city is a tiny pavilion inside one cell. The knight is a tiny rider inside the selected cell. "
     "Mist only in the unexplored cells. No European oil paint."),
    ("10-mural", "Mexican muralism",
     "The whole screenshot is a Mexican mural. Broad flat planes, earth red, cactus green, indigo, and strong outlines. "
     "Each cell is a patch of that mural. Hills are simple mounds. The city is a solid small temple. The knight is a solid figure inside the selected cell, monumental in shape but still cell-sized. "
     "The portrait box is a larger mural head. No prettiness and no haze."),
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
        print(f"OK {slug} {img.size}", flush=True)
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
    out = "/opt/cursor/artifacts/moodboard/painted-screen-board.jpg"
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
