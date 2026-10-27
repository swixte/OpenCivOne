"""Round 4: pieces fill their cell, terrain paint carries the style, grid stays locked."""

import base64
import io
import json
import os
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed

from PIL import Image

OUT = "/tmp/mood-r4"
os.makedirs(OUT, exist_ok=True)

BOARD = (
    "A top-down Civilization game screen. Camera straight down. No sky, no horizon. "
    "A left sidebar, then a board of large squares, exactly 8 columns and 6 rows. "
    "Thin dark straight grid lines. Every square is one solid terrain, edge to edge. "
    "The coast is a staircase of whole squares: a square is all grass, all forest, all hills, all sand, or all ocean. "
    "Never a diagonal beach. Never a square that is half water. "
    "One city sprite and one knight sprite. Each sits in its own grass square and fills about three quarters of that square, "
    "with only a slim margin of grass between the sprite and the grid lines. Neither sprite touches or crosses a grid line. "
    "The city is several roofs and a tower, a single game piece, not a town spread over many squares. "
    "The knight is a rider with a lance, large enough to read, and a bright yellow selection rectangle lies exactly on his square's border. "
    "Board, left to right: "
    "forest forest grass grass hills hills ocean ocean / "
    "forest forest grass grass grass hills sand ocean / "
    "grass grass grass CITY grass grass sand ocean / "
    "grass grass grass grass KNIGHT grass sand ocean / "
    "grass grass grass grass grass sand ocean ocean / "
    "grass hills hills grass sand ocean ocean ocean. "
    "Contrast is strong. Grass is bright yellow-green, forest is far darker, hills are tan, sand is cream, ocean is saturated blue. "
    "Grass is painted by hand: neighboring grass squares share a color but not an identical stamp. "
    "Sidebar: a small world minimap, a portrait in the same style, and four bars in gold, crimson, blue, and green. No letters, no numbers."
)

STYLES = [
    ("01-ukiyo",
     "Hand-printed Japanese nishiki-e, Hokusai, on cream paper. Black keyline, flat ink, a little bokashi, visible woodgrain, the black plate slightly off-register. "
     "Not vector art and not watercolor. Forest squares are large Hokusai pines that fill the square. Ocean squares are the Great Wave, filling the square. "
     "Hill squares are ochre contour mountains filling the square. The city roofs are cinnabar and the city is big in its square. "
     "The knight is drawn with the same keyline and is big in his square. The portrait is a woodblock bust of a Japanese lord."),
    ("02-classic",
     "Classical oil in the clear honey-colored daylight of Claude Lorrain. Fine luminous brush, orderly, airy, not dark and not thick impasto. "
     "Each square is a separate patch of that paint, stopping hard at the grid. Grass squares are bright sunlit meadow, each brushed a little differently. "
     "Forest squares are a deep even green. Ocean squares are clean lapis, edge to edge. "
     "The city is a pale classical citadel with terracotta roofs, large in its square, evenly lit. "
     "The knight is a classical cavalryman, large in his square. The portrait is a calm marble-like profile with a laurel, on a light ground. "
     "This must not look Baroque or muddy."),
    ("03-baroque",
     "Baroque oil with heavy impasto and chiaroscuro, in the manner of Velázquez. Thick paint ridges you can see. "
     "Each square stops hard at the grid. Grass is warm gold-green, bright enough to read, with a lit side and a shadowed side inside the square. "
     "Forest is green-black. Ocean is deep ultramarine worked with a loaded brush, edge to edge, no diagonal shore. "
     "The city is large in its square, stone lit from the upper left, the far side in brown shadow, roofs dull crimson. "
     "The knight is large, with a crimson cloak. The portrait is a Baroque half-length on a dark ground, face strongly lit, ornate dark frame. "
     "Darker and heavier than a classical daylight map, while grass, forest, hills, and sea stay easy to tell apart."),
]


def generate(key, slug, style):
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
        return slug, fatal
    img = Image.open(io.BytesIO(base64.b64decode(payload["data"][0]["b64_json"]))).convert("RGB")
    img.save(path, quality=92)
    print(f"OK {slug} {img.size}", flush=True)
    return slug, False


def main():
    key = os.environ["OPENAI_API_KEY"]
    with ThreadPoolExecutor(3) as pool:
        futs = [pool.submit(generate, key, s, p) for s, p in STYLES]
        for fut in as_completed(futs):
            slug, fatal = fut.result()
            if fatal:
                print("FATAL", slug, flush=True)
                return


if __name__ == "__main__":
    main()
