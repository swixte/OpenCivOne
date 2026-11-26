"""Round 3: stair-step coast, one piece per cell, styles pushed apart."""

import base64
import io
import json
import os
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed

from PIL import Image

OUT = "/tmp/mood-r3"
os.makedirs(OUT, exist_ok=True)

BOARD = (
    "A top-down Civilization game screen, camera straight down, no sky and no horizon. "
    "Left sidebar, then a board of very large squares: exactly 8 columns and 6 rows, like a chessboard of terrain. "
    "Every square is one solid terrain from edge to edge. Terrain never blends across a grid line. "
    "The shoreline is a staircase of whole squares. No diagonal beach, no foam cutting across a grass square, "
    "no square that is half water and half land. A square is entirely grass, entirely forest, entirely hills, "
    "entirely sand, or entirely ocean. "
    "Grid lines are thin, dark, and straight. "
    "There is one city and it is a game sprite: a few pale buildings with red roofs clustered in the CENTER of a single grass square, "
    "about half the size of that square, not touching the grid lines, not entering any neighboring square. Not a sprawling town. "
    "There is one knight on a horse, a game sprite in the CENTER of a different grass square, not touching the grid lines. "
    "A bright yellow rectangle is drawn exactly on that knight's square and nowhere else. "
    "Board, left to right, top to bottom: "
    "forest forest grass grass hills hills ocean ocean / "
    "forest forest grass grass grass hills sand ocean / "
    "grass grass grass CITY grass grass sand ocean / "
    "grass grass grass grass KNIGHT grass sand ocean / "
    "grass grass grass grass grass sand ocean ocean / "
    "grass hills hills grass sand ocean ocean ocean. "
    "High contrast: grass bright yellow-green, forest much darker green, hills sandy tan, sand pale cream, ocean saturated cobalt. "
    "Sidebar: a small world minimap, a portrait, and four horizontal bars in gold, crimson, blue, and green. No letters and no numbers."
)

STYLES = [
    ("01-ukiyo",
     "Art style is a real Japanese nishiki-e woodblock, Hokusai, printed on cream paper. Flat ink with a black keyline and a little bokashi gradation, "
     "plus visible woodgrain and a slight off-register of the black plate, so it looks printed by hand rather than drawn as vector icons. "
     "Forest tiles are Hokusai pines. Ocean tiles are the Great Wave pattern, each tile a full square of waves. Hill tiles are contour mountains in ochre and black. "
     "Grass tiles are a printed straw-green field with varied carved strokes, still bright and flat. The city roofs are cinnabar. The portrait is a woodblock bust."),
    ("02-hiroshige",
     "Art style is Hiroshige: a colored woodblock with broader, quieter fields of indigo, moss green, pale straw, and a warm cream sky-less ground. "
     "Still a print, with a fine keyline and soft bokashi inside each tile, not a watercolor landscape and not a European painting. "
     "Each ocean tile is a full square of deep indigo with white wave crests. Forest tiles are dark blue-green pine. "
     "The city roof is a strong cinnabar red so it separates from the grass. High contrast between tile types. The portrait is a quiet woodblock bust."),
    ("03-classic",
     "Art style is classical oil in the clear daylight of Claude Lorrain and Poussin: honey-colored light, orderly, airy, visible but fine brushwork. "
     "Each square is its own patch of paint that stops at the grid line. Grass tiles are bright sunlit yellow-green. Forest tiles are a dense dark green. "
     "Ocean tiles are clean lapis blue, edge to edge. Sand tiles are pale cream, edge to edge. No diagonal shoreline. "
     "The city is a small pale classical citadel with a terracotta roof, lit evenly. The portrait is a calm classical profile. "
     "This must not look dark, foggy, or Baroque."),
    ("04-baroque",
     "Art style is Baroque oil with thick impasto and chiaroscuro, like a Velázquez painting turned into a game board. "
     "Each square is its own patch of heavy paint that stops at the grid line. No diagonal shoreline. "
     "Grass tiles are warm gold-green and bright enough to read. Forest tiles are nearly black-green. Ocean tiles are deep ultramarine with white paint dragged in the foam, edge to edge. "
     "The citadel has a lit face and a dark side, with a dull crimson roof. The knight's cloak is crimson. The portrait is a Baroque half-length with a dark ground and a strong highlight on the face. "
     "Heavier, darker, and more dramatic than a classical landscape, while the tile colors stay easy to tell apart."),
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
        return slug, None, fatal
    img = Image.open(io.BytesIO(base64.b64decode(payload["data"][0]["b64_json"]))).convert("RGB")
    img.save(path, quality=92)
    print(f"OK {slug} {img.size}", flush=True)
    return slug, path, False


def main():
    key = os.environ["OPENAI_API_KEY"]
    with ThreadPoolExecutor(4) as pool:
        futs = [pool.submit(generate, key, s, p) for s, p in STYLES]
        for fut in as_completed(futs):
            slug, path, fatal = fut.result()
            if fatal:
                print("FATAL", slug, flush=True)
                return


if __name__ == "__main__":
    main()
