"""Top-down painted game maps. No horizon, no landscape with a grid laid over it."""

import base64
import io
import json
import os
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed

from PIL import Image, ImageDraw, ImageFont

OUT = "/opt/cursor/artifacts/moodboard/topdown"
os.makedirs(OUT, exist_ok=True)

SCENE = (
    "Orthographic top-down strategy map, camera straight down. The frame is entirely ground. "
    "No horizon, no sky, no vanishing point, no panorama, no view from a hill. "
    "A square grid of map cells, about twelve across and eight down, each cell a patch of painted ground. "
    "Neighboring cells of the same terrain flow together: a forest is one canopy across several cells, the sea is one body of water. "
    "The grid lines are faint and follow the cells. They are not drawn over a picture of a landscape. "
    "Terrain: grassland, forest, hills, a coast, ocean. "
    "A walled city is a cluster of roofs seen from directly above, sitting in a few cells. "
    "A rider is seen from directly above, small, inside one cell, with a thin selection square on that cell. "
    "A narrow left margin, in the same paint, holds a tiny overview and a small portrait. No words, letters, or numbers. "
)

STYLES = [
    ("01-ukiyo-color", "Ukiyo-e, color",
     "Painted Japanese woodblock seen from straight above. Flat pigment with woodgrain and a little ink bleed, and a black keyline on the large shapes. "
     "Grass cells are straw and olive printed strokes. Forest is round green canopies. Hills are simple ochre mounds. "
     "The ocean is the carved wave pattern filling the water cells, seen from above, not a horizon. Roofs are blue-grey rectangles. "
     "The rider is a small top-down printed figure. No Fuji, no sky band, no side view of a coast."),
    ("02-ukiyo-soft", "Ukiyo-e, soft pigment",
     "A softer painted woodblock seen from straight above, pigment soaked into paper. Muted indigo, moss green, cinnabar, and pale straw, with edges that bleed slightly. "
     "Grass is a varied wash, not a repeated stamp. Trees are soft dark canopies. Water is layered blue strokes filling the ocean cells. "
     "The city roofs and the rider are painted in the same soaked pigment, from directly above. No rain over a landscape, no sky, no perspective."),
    ("03-classicism", "Classicism",
     "Classical oil paint seen from straight above, in the palette of Claude Lorrain: golden ochre grass, olive groves, warm stone roofs, ultramarine water, soft blended edges. "
     "Light falls from the upper left across the tops of canopies and roofs. Brushstrokes are visible in the meadow. "
     "The whole frame is this painted ground. No sky, no sunset, no distant mountains on a horizon, no view from above a cliff."),
    ("04-baroque", "Baroque",
     "Baroque oil paint seen from straight above. Thick visible brush in the grass, warm light on one side of each roof and each tree mass, brown shadow on the other. "
     "Ochre and olive ground, dark blue-green water with thick painted highlights, dull crimson on the rider, cream and umber roofs. "
     "A real painted surface, not a dark interface. No sky, no sunbeams, no panorama, no large portrait filling the map."),
]


def font(size):
    for path in ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                 "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf"):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def generate(key, slug, title, style):
    path = os.path.join(OUT, slug + ".jpg")
    body = json.dumps({
        "model": "gpt-image-2",
        "prompt": SCENE + style,
        "size": "1536x1024",
        "quality": "medium",
        "n": 1,
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
        fatal = e.code in (401, 402, 403, 429) or any(
            w in text.lower() for w in ("billing", "quota", "insufficient", "budget"))
        return None, text[:180], fatal
    img = Image.open(io.BytesIO(base64.b64decode(payload["data"][0]["b64_json"]))).convert("RGB")
    img.save(path, quality=90)
    print(f"OK {slug} {img.size}", flush=True)
    return path, title, False


def board(plates):
    thumb_w, thumb_h, cap = 700, 466, 32
    rows = (len(plates) + 1) // 2
    sheet = Image.new("RGB", (thumb_w * 2, rows * (thumb_h + cap)), (24, 24, 24))
    draw = ImageDraw.Draw(sheet)
    label = font(18)
    for i, (path, title) in enumerate(plates):
        im = Image.open(path).convert("RGB")
        im.thumbnail((thumb_w, thumb_h), Image.LANCZOS)
        x, y = (i % 2) * thumb_w, (i // 2) * (thumb_h + cap)
        sheet.paste(im, (x, y))
        draw.text((x + 12, y + im.height + 6), title, fill=(240, 236, 228), font=label)
    out = "/opt/cursor/artifacts/moodboard/topdown.jpg"
    sheet.save(out, quality=90)
    print("BOARD", out, flush=True)


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
