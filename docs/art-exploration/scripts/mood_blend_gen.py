"""Generate seamless top-down moodboard maps. Reads OPENAI_API_KEY from the environment. Never prints the key."""

import base64
import io
import json
import os
import sys
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed

from PIL import Image

OUT = "/tmp/mood-blend"
os.makedirs(OUT, exist_ok=True)

COMMON = """
A widescreen historical-strategy game screen photographed from directly overhead. Orthographic, straight down. No sky, no horizon, no vanishing point, no distant landscape. The map area is entirely ground.

The map is ONE continuous painting, not a board of squares. Grass flows into grass as a single meadow: the same color and the same brushwork cross every place where a hidden square edge might have been. Forest is one canopy mass with a soft irregular treeline. Hills are real landforms rising out of the grass, not squares of hill texture. The sea is one body of water with a curving coastline. There are no tile borders, no grout lines, no dark seams, no chessboard, no graph paper, no rectangular patches, and no change in texture along straight horizontal or vertical lines. Do not draw a map grid.

Layout of the painting, with irregular painted edges:
- Upper left of the map: one deep forest, a single connected canopy, its edge a curved treeline melting into grass.
- The broad center: one bright open meadow, continuous in every direction.
- Upper right of the land, before the water: one tan hill ridge, a landform, blending into the meadow. A second smaller hill rises at the bottom edge and also blends into grass.
- The right side: one saturated blue sea. The shoreline bows and curves. It is not a straight vertical cut.

SCALE: Both sprites are small tokens on a huge meadow. The citadel is ONE short keep, about as tall as it is wide, with a single red roof. Not a tall spire. No side chapels, no outer courtyard, no town, no wings. The whole keep, roof included, is about the same size as the horse. The rider is one person on one horse, small, with no tall lance and no banner pole, seen from a slight high angle so both are readable.

PLACEMENT: The exact center of the picture is empty grass. Counting only the map and not the sidebar, the short round keep stands thirty-one percent of the way across the map. Its stone base, where the wall meets the grass, is twenty-eight percent of the way down the picture, so the whole keep including its shadow sits in open grass with grass visible under it and above it. It is not in the forest and not on the hill. The horse's hooves are sixty-eight percent of the way across the map and seventy-two percent of the way down the picture. Grass surrounds the nose, the tail, and the hooves. The horse is far from the sea.

A single thin bright yellow rectangle hugs the rider, with only a slim margin of grass between the horse and the rectangle. The rectangle is only a little larger than the horse. It does not touch the citadel. That yellow rectangle is the only straight outline on the map. No other rectangles, no lattice, no highlighted squares.

The left sixth of the image is a sidebar outside the map, divided from the painting by one clean vertical edge. Top to bottom: a small world minimap with no labels and no grid; a portrait; four long horizontal bars stacked in this order: gold, crimson, blue, green. The bars contain no marks.

Nowhere in the image is there any letter, number, word, caption, or watermark.
""".strip()

STYLES = {
    "ukiyo-e": """
Style: a hand-printed Japanese nishiki-e woodblock in the manner of Hokusai, on warm cream paper, with visible woodgrain. Flat unmodulated color. Black sumi keylines outline only objects: pine trunks and needles, the citadel's eaves, the rider, and the curling crests of the waves. The grassland has no black border and is one printed field of pale yellow-green strokes. The forest is a grove of black-green pines sharing one canopy. The sea is the Great Wave: Prussian blue water and white claw-like crests flowing as one ocean, not a wave motif stamped into boxes. Roofs are cinnabar red. Hills are pale tan with fine contour hatching that follows the ridge, not the edge of a square. The sidebar is the same cream paper. The portrait is a stern man with a topknot, flat color, black keyline, like an actor print. No cast shadows, no European oil haze, no perspective depth.
""".strip(),
    "classicism": """
Style: a daylight oil painting in the manner of Claude Lorrain and Nicolas Poussin. Honey-colored sunlight, fine smooth brushwork, clear air, one canvas. The meadow is a bright yellow-green, high and even, strokes traveling across the whole field. The forest is a deep green mass, much darker than the grass, with a feathery irregular edge. The sea is lapis lazuli, saturated and continuous. Hills are warm tan earth. The citadel is one short pale limestone keep, a single compact round tower with one terracotta roof, no spire and no side buildings, lit by the same honey light. The rider wears a red cloak on a light horse. The sidebar is pale stone-colored. The portrait is a marble-like classical profile in an oval medallion. No tile mosaic, no encaustic squares, no map graticule.
""".strip(),
    "baroque": """
Style: an oil painting in the manner of Velázquez. Heavy impasto, loaded brush, chiaroscuro, but the map stays high-contrast and readable. The meadow is warm gold-green and bright where the light falls; the light is one broad wash, not a checkerboard of light and dark squares. The forest is green-black, much darker than the grass, one canopy. The sea is deep ultramarine, saturated, one water. Hills are ochre and tan, modeled as round landforms. The citadel is one short pale stone keep, lit on the upper left and shadowed on the lower right, with a single crimson roof, no tall spire and no side wings. The rider wears a crimson cloak that reads immediately against the grass. The sidebar is a dark ground. The portrait is a dark-ground oil head and shoulders, face lit, crimson cloak, brown-black background. Brushstrokes run across the meadow without stopping on straight lines. Do not sink the whole image into brown mud. Do not alternate light and dark squares.
""".strip(),
}


PLACEMENTS = {
    "i": (
        "Repeat: the bottom of the round keep is at twenty-eight percent down, not at one third down. "
        "The hooves of the horse are at seventy-two percent down and sixty-eight percent across. Grass under the hooves and under the keep."
    ),
    "j": (
        "Repeat: squat round tower, base at twenty-eight percent of the picture height, thirty-one percent across the map. "
        "Horse hooves at seventy-two percent of the picture height and sixty-eight percent across the map. No lance."
    ),
}


def generate(key, slug, variant="a"):
    prompt = COMMON + "\n\n" + STYLES[slug] + "\n\n" + PLACEMENTS[variant]
    tag = f"{slug}-{variant}"
    path = os.path.join(OUT, f"{tag}.png")
    meta = os.path.join(OUT, f"{tag}.prompt.txt")
    with open(meta, "w", encoding="utf-8") as f:
        f.write(prompt)
    body = json.dumps({
        "model": "gpt-image-2",
        "prompt": prompt,
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
        data = payload["data"][0]
        if "b64_json" in data:
            raw = base64.b64decode(data["b64_json"])
        elif "url" in data:
            with urllib.request.urlopen(data["url"], timeout=120) as img_resp:
                raw = img_resp.read()
        else:
            print(f"FAIL {slug} no image field", flush=True)
            return slug, False, "no image field"
        img = Image.open(io.BytesIO(raw)).convert("RGB")
        img.save(path)
        jpg = os.path.join(OUT, f"{tag}.jpg")
        img.save(jpg, quality=92)
        print(f"OK {tag} {img.size[0]}x{img.size[1]}", flush=True)
        return tag, True, path
    except urllib.error.HTTPError as e:
        text = e.read().decode(errors="replace")
        fatal = e.code in (401, 402, 403, 429) or any(
            w in text.lower() for w in ("billing", "quota", "insufficient", "budget")
        )
        print(f"FAIL {tag} HTTP {e.code} fatal={fatal} {text[:300]}", flush=True)
        return tag, False, f"fatal={fatal} HTTP {e.code}"
    except Exception as e:
        print(f"FAIL {tag} {type(e).__name__}: {e}", flush=True)
        return tag, False, str(e)


def main():
    key = os.environ["OPENAI_API_KEY"]
    args = sys.argv[1:]
    if not args:
        jobs = [(s, v) for s in STYLES for v in PLACEMENTS]
    else:
        jobs = []
        for a in args:
            if "-" in a:
                s, v = a.rsplit("-", 1)
                jobs.append((s, v))
            else:
                jobs.append((a, "a"))
    fatal = False
    with ThreadPoolExecutor(max_workers=3) as pool:
        futs = [pool.submit(generate, key, slug, variant) for slug, variant in jobs]
        for fut in as_completed(futs):
            tag, ok, info = fut.result()
            if not ok and isinstance(info, str) and info.startswith("fatal=True"):
                fatal = True
    if fatal:
        sys.exit(3)


if __name__ == "__main__":
    main()
