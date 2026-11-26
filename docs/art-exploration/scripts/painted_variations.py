"""Four more painted game boards: two ukiyo-e, classicism, and Baroque. The map is one painting."""

import base64
import io
import json
import os
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed

from PIL import Image, ImageDraw, ImageFont

OUT = "/opt/cursor/artifacts/moodboard/painted-variations"
os.makedirs(OUT, exist_ok=True)

SCENE = (
    "A widescreen view of a strategy game played on top of one continuous painting. "
    "The painting fills the screen and is the thing you see. Terrain flows across the whole map without breaking into repeated icons or stamps. "
    "A faint square grid is drawn lightly over the painting and does not cut it into separate pictures. "
    "A narrow margin on the left, painted in the same medium, holds a tiny world sketch and a small portrait. It is part of the painting, not a modern interface panel. "
    "No words, letters, or numbers. "
    "In the landscape, small: a walled city with a temple, and a rider on a horse with a thin selection line around him. A coast. The far distance softens. "
    "The rider and the city are painted with the same brush as the land. They are not sprites and not clipart. "
)

STYLES = [
    ("01-ukiyo-hokusai", "Ukiyo-e, Hokusai",
     "A full-color Japanese woodblock in the manner of Hokusai, but painted, not diagrammed. Large fields of color, a graded bokashi sky from indigo to pale straw, visible woodgrain and slight ink bleed. "
     "The sea is one great area of repeating carved waves, continuous across many grid squares. The land is one hillside of straw and green, with one grove of pines, not a tile of identical trees. "
     "Black keyline only on the large shapes. The rider is drawn at the scale of the trees, with the same line and flat pigment. No video-game interface, no tiny repeated stamps."),
    ("02-ukiyo-hiroshige", "Ukiyo-e, Hiroshige",
     "An atmospheric woodblock in the manner of Hiroshige. The painting is mostly weather: a graded blue-grey sky, fine diagonal rain, mist where the land meets a pale sea. "
     "Color is soft and layered, like pigment soaked into paper, with a few deep indigo and cinnabar accents. The land is one quiet shore and one dark grove. "
     "The city and the rider are small printed figures in that weather, with a sure but thin line. The grid is barely there, a few pale rules. No flat icon tiles, no bright game HUD."),
    ("03-classicism", "Classicism",
     "A classical oil landscape in the manner of Claude Lorrain. Clear golden light from the left, a big luminous sky over the upper half, airy blue distance, warm umber in the foreground, a few ultramarine and vermilion accents. "
     "Smooth paint, large simple masses, trees or a ruin framing one side. The whole map is this one landscape. The city is a small pale temple in the middle distance. The rider is a small figure on the foreground road, painted in the same soft oil. "
     "The grid is a faint warm line. No darkness, no storm, no chrome, no game icons."),
    ("04-baroque", "Baroque",
     "A Baroque oil landscape in the manner of Rubens and Velazquez. Visible brushstrokes, soft edges, thin brown glazes, thicker cream paint in the light. A broad grey-cream clouded sky with ordinary daylight, no sunbeams. "
     "The land is one sweep of ochre grass and dark trees, paint continuing across the grid. The city is loosely brushed pale architecture. The rider is small but fully painted: a fleshy face, a dark horse, a crumpled crimson sash, dull armor. "
     "The portrait in the margin is a real head in the same paint. No poster lighting, no polished metal, no dark user-interface skin, no sprites."),
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
    body = json.dumps({
        "model": "gpt-image-2", "prompt": prompt, "size": "1536x1024", "quality": "medium", "n": 1,
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
    sheet = Image.new("RGB", (thumb_w * 2, (thumb_h + cap) * 2), (24, 24, 24))
    draw = ImageDraw.Draw(sheet)
    label = font(18)
    for i, (path, title) in enumerate(plates):
        im = Image.open(path).convert("RGB")
        im.thumbnail((thumb_w, thumb_h), Image.LANCZOS)
        x, y = (i % 2) * thumb_w, (i // 2) * (thumb_h + cap)
        sheet.paste(im, (x, y))
        draw.text((x + 12, y + im.height + 6), title, fill=(240, 236, 228), font=label)
    out = "/opt/cursor/artifacts/moodboard/painted-variations.jpg"
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
