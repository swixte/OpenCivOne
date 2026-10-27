"""One moodboard plate per style. Reads OPENAI_API_KEY from the environment. Does not write the key anywhere."""

import base64
import io
import json
import os
import sys
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed

from PIL import Image, ImageDraw, ImageFont

OUT = "/opt/cursor/artifacts/moodboard"
os.makedirs(OUT, exist_ok=True)

SCENE = (
    "A single scene for a historical strategy game: open grassland, a small walled city with one columned temple, "
    "and an armoured knight on a barded horse in the foreground. Large simple shapes that stay readable when small. "
    "No Latin letters, no numbers, no caption, no border, no frame."
)

STYLES = [
    ("01-baroque", "Baroque",
     "Baroque oil painting in the manner of Caravaggio and Rubens. One hard light from the upper left, everything else falling into warm brown-black. "
     "Bodies and cloth in motion, crimson and gold against ivory, thick paint, large simple shapes. Theatrical, never evenly lit."),
    ("02-ukiyoe", "Ukiyo-e",
     "Japanese woodblock print in the manner of Hokusai. A sure black keyline around every shape, flat unmodulated color, no cast shadows and no haze. "
     "Indigo, Prussian blue, cinnabar, pale straw and sumi black. Water and foliage are repeating carved patterns."),
    ("03-mughal", "Mughal miniature",
     "Mughal court miniature. Opaque watercolor and gold, flat stacked perspective, every figure and building fully lit, no cast shadow and no haze. "
     "Malachite green, lapis blue, saffron, cinnabar and gold. The ground is a carpet of drawn plants. Detail lives inside one clear silhouette."),
    ("04-deco", "Art Deco",
     "Art Deco relief painting. Streamlined volumes, sunbursts and chevrons, stone and metal as flat planes with one hard highlight. "
     "Black, antique gold, teal, ivory and a stripe of coral. A temple and a horse share the same bevel and the same line."),
    ("05-babylon", "Glazed brick",
     "The entire scene is Neo-Babylonian glazed brick, like the Ishtar Gate. Cobalt blue brick courses form the ground, the walls and the sky. "
     "The knight, the horse and the temple are gold and turquoise relief figures raised from the brick, with white rosettes. No realistic painting, no perspective depth."),
    ("06-codex", "Mesoamerican codex",
     "A Mesoamerican screenfold codex painted on fig-bark. Flat Maya blue, red and yellow, figures strictly in profile, the city drawn as a glyph, the ground as a flat band. "
     "No cast shadow, no horizon, no European perspective. Ornamental glyphs may appear only as pattern."),
    ("07-palekh", "Palekh lacquer",
     "A Russian Palekh lacquer miniature. The scene floats on a deep black lacquer ground. Figures and the temple are bright egg-tempera in jewel red, emerald and gold, "
     "with fine gold hatching. Small, precious and enamel-like, surrounded by black."),
    ("08-egypt", "Egyptian tomb",
     "Ancient Egyptian tomb painting in horizontal registers, like the tomb of Nebamun. Bodies in profile, mineral colors: red-brown skin, pale gold, malachite green, lapis blue. "
     "Flat, no perspective, no cast shadow. The grassland is a band of reeds, the temple is a pylon, the knight is a figure in a chariot."),
    ("09-shanshui", "Blue-green landscape",
     "Song-dynasty blue-and-green landscape, mineral pigment on silk, like A Thousand Li of Rivers and Mountains. Azurite peaks, malachite slopes, gold mist, empty silk. "
     "The knight and the temple are small against the mountains. No oil-paint texture, no European lighting."),
    ("10-mural", "Mexican muralism",
     "A Mexican mural in the manner of Diego Rivera. Monumental solid bodies, earth red, cactus green and indigo. Ancient stone and the temple share the wall with the weight of a people. "
     "Broad flat planes of color, strong outlines, no prettiness, no haze."),
]


def font(size):
    for path in ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                 "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf"):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def one(key, slug, title, style):
    path = os.path.join(OUT, slug + ".png")
    prompt = f"Style: {style} Subject: {SCENE}"
    last = ""
    for quality in ("low", "medium"):
        body = json.dumps({"model": "gpt-image-2", "prompt": prompt, "size": "1024x1024", "quality": quality, "n": 1}).encode()
        req = urllib.request.Request(
            "https://api.openai.com/v1/images/generations",
            data=body,
            headers={"Authorization": f"Bearer {key}", "Content-Type": "application/json"},
            method="POST",
        )
        try:
            with urllib.request.urlopen(req, timeout=300) as resp:
                payload = json.loads(resp.read().decode())
            img = Image.open(io.BytesIO(base64.b64decode(payload["data"][0]["b64_json"]))).convert("RGB")
            img.save(path)
            print(f"OK {slug} quality={quality}", flush=True)
            return path, title, False
        except urllib.error.HTTPError as e:
            text = e.read().decode(errors="replace")
            last = f"HTTP {e.code} {text[:240]}"
            print(f"FAIL {slug} quality={quality} {last}", flush=True)
            fatal = e.code in (401, 402, 403, 429) or any(w in text.lower() for w in ("billing", "quota", "insufficient", "budget"))
            if fatal or "quality" not in text.lower():
                return None, last, fatal
        except Exception as e:
            last = str(e)
            print(f"FAIL {slug} quality={quality} {last}", flush=True)
            return None, last, False
    return None, last, False


def board(plates):
    thumb = 512
    cap = 36
    cols = 5
    rows = (len(plates) + cols - 1) // cols
    W, H = cols * thumb, rows * (thumb + cap)
    sheet = Image.new("RGB", (W, H), (24, 24, 24))
    draw = ImageDraw.Draw(sheet)
    label = font(18)
    for i, (path, title) in enumerate(plates):
        im = Image.open(path).convert("RGB")
        im.thumbnail((thumb, thumb), Image.LANCZOS)
        x, y = (i % cols) * thumb, (i // cols) * (thumb + cap)
        sheet.paste(im, (x + (thumb - im.width) // 2, y))
        draw.text((x + 12, y + thumb + 8), title, fill=(240, 236, 228), font=label)
    out = os.path.join(OUT, "moodboard.jpg")
    sheet.save(out, quality=90)
    print(f"BOARD {out} {W}x{H}", flush=True)


def main():
    key = os.environ["OPENAI_API_KEY"]
    plates = []
    with ThreadPoolExecutor(4) as pool:
        futs = [pool.submit(one, key, slug, title, style) for slug, title, style in STYLES]
        fatal = False
        for fut in as_completed(futs):
            path, info, stop = fut.result()
            if path is None:
                print("MISS", info, flush=True)
                fatal = fatal or stop
                continue
            plates.append((path, info))
    order = {s[0] + ".png": i for i, s in enumerate(STYLES)}
    plates.sort(key=lambda p: order[os.path.basename(p[0])])
    if plates:
        board(plates)
    print(f"DONE {len(plates)} of {len(STYLES)}", flush=True)
    if len(plates) < len(STYLES):
        sys.exit(2)


if __name__ == "__main__":
    main()
