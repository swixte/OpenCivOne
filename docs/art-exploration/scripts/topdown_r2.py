"""Round 2: large cells, one city in one square, high contrast, painted styles."""

import base64
import io
import json
import os
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed

from PIL import Image

OUT = "/tmp/mood-r2"
os.makedirs(OUT, exist_ok=True)

BOARD = (
    "A top-down Civilization strategy-game screenshot. Camera straight down. No sky, no horizon, no vanishing point. "
    "The right four fifths of the image is a board of LARGE equal squares, exactly 8 columns and 6 rows. "
    "Grid lines are thin, dark, and straight, and every terrain change happens on a grid line. "
    "Nothing crosses a grid line. Each square holds exactly one kind of ground. "
    "A city is one game piece: a single small castle with a red roof, sitting in the middle of ONE grass square, "
    "with empty grass between the castle and that square's edges. It is not a town, not a district, and it does not touch a second square. "
    "A knight on horseback is one game piece, sitting in the middle of ONE other grass square, with a bright yellow selection rectangle "
    "drawn exactly on that square's border. The knight does not touch a second square. "
    "There is only one castle and only one knight. "
    "Layout of the 8 by 6 board, left to right, top to bottom: "
    "row 1: forest, forest, grass, grass, hills, hills, ocean, ocean. "
    "row 2: forest, forest, grass, grass, grass, hills, coast, ocean. "
    "row 3: grass, grass, grass, CASTLE, grass, grass, coast, ocean. "
    "row 4: grass, grass, grass, grass, KNIGHT, grass, coast, ocean. "
    "row 5: grass, grass, grass, grass, grass, coast, ocean, ocean. "
    "row 6: grass, hills, hills, grass, coast, ocean, ocean, ocean. "
    "Colors are high contrast and easy to tell apart at a glance: grass is bright yellow-green, forest is deep green and much darker, "
    "hills are sandy tan, coast is pale sand, ocean is saturated cobalt blue. The castle is pale stone with a red roof. "
    "The left fifth is a sidebar outside the board: a small world minimap, a portrait of the same knight, and four plain horizontal bars "
    "colored gold, crimson, blue, and green. No letters, numbers, or words anywhere."
)

STYLES = [
    ("01-ukiyo", "Ukiyo-e",
     "The art is a Japanese woodblock print, nishiki-e, like Hokusai. Flat areas of ink, a firm black keyline around every tree, hill, wave, castle, and the knight. "
     "Cream paper. Forest squares are clusters of black-outlined pines. Ocean squares are the carved Great Wave pattern in deep blue and white. "
     "Grass is a flat straw-green print, not a soft wash. The portrait is a woodblock bust. Bold, graphic, high contrast. Not watercolor, not a European oil painting."),
    ("02-classic", "Classicism",
     "The art is classical oil paint in the manner of Claude Lorrain and Poussin, but used as a bright game board, not a hazy landscape. "
     "Visible brushstrokes. Grass is sunlit yellow-green. Forest is a dark green copse with a clear edge. Sea is clean ultramarine. "
     "The castle is warm limestone and a terracotta roof, lit from the upper left. The knight is a small classical rider. "
     "Values stay separated: you can tell grass, forest, hills, and sea apart instantly. No brown fog, no sky, no sunset."),
    ("03-baroque", "Baroque",
     "The art is 17th-century Baroque oil, thick paint like Velázquez, used as a bright game board. "
     "Strong light from the upper left, real shadow, but the board stays readable. Grass is warm and bright. Forest is deep green-black. "
     "Sea is dark ultramarine with white foam along the coast squares. The castle is lit stone and a dull crimson roof. "
     "The knight wears a crimson cloak. Brush is visible and edges between terrain types stay crisp. Not a brown fog, not graphic-novel lighting, no sky."),
    ("04-ukiyo-soft", "Ukiyo-e, softer ink",
     "The art is a painted ukiyo-e woodblock with slightly softer pigment bleed, still graphic. Indigo, moss, cinnabar, and straw on cream paper, with a black keyline. "
     "Ocean squares are layered wave strokes in strong blue. Forest squares are dark pine masses that stop at the grid line. "
     "The castle roof is cinnabar so it reads immediately. High contrast between grass, forest, and sea. No horizon, no rain scene, no perspective landscape."),
]


def generate(key, slug, title, style):
    path = os.path.join(OUT, slug + ".jpg")
    body = json.dumps({
        "model": "gpt-image-2",
        "prompt": BOARD + " " + style,
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
        print(f"FAIL {slug} HTTP {e.code} {text[:300]}", flush=True)
        fatal = e.code in (401, 402, 403, 429) or any(
            w in text.lower() for w in ("billing", "quota", "insufficient", "budget"))
        return slug, None, fatal
    img = Image.open(io.BytesIO(base64.b64decode(payload["data"][0]["b64_json"]))).convert("RGB")
    img.save(path, quality=92)
    print(f"OK {slug} {img.size}", flush=True)
    return slug, path, False


def main():
    key = os.environ["OPENAI_API_KEY"]
    with ThreadPoolExecutor(4) as pool:
        futs = [pool.submit(generate, key, s, t, p) for s, t, p in STYLES]
        for fut in as_completed(futs):
            slug, path, fatal = fut.result()
            if fatal:
                print("FATAL", slug, flush=True)
                return


if __name__ == "__main__":
    main()
