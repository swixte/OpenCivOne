"""Generate the remaining ukiyo-v2 ground textures and mine sprite. Key via env only."""
import base64, json, os, sys, time, urllib.request, urllib.error
from concurrent.futures import ThreadPoolExecutor

OUT = '/tmp/mood-comp/raw'
COUNT_PATH = '/tmp/mood-comp/api-count.txt'
os.makedirs(OUT, exist_ok=True)

TOP = (
    "Seen straight down from directly above like a map, orthographic, no horizon, no perspective, no sky. "
    "The pattern fills the entire image evenly edge to edge, no border, no frame, no buildings, no people, "
    "no large trees, no animals, no text, no seals, no signature. "
)
A_STYLE = (
    "A Japanese ukiyo-e woodblock print figure in the manner of Hokusai: completely flat colour areas with no shading, "
    "no gradients, no highlights, no glossy rendering, strong even black keyline, limited palette of indigo, cinnabar red, "
    "ochre and cream only, slight visible woodgrain in the flat colour, slight print registration offset between the colour and the keyline. "
)
ANGLE = (
    "Seen from a high three-quarter angle (camera above and in front, looking down at about 60 degrees). "
    "Bold simple silhouette, readable at small size. Centered, fills most of the frame, fully visible, nothing cut off. "
    "Background: perfectly flat solid pure magenta #FF00FF everywhere around it, no grass mound, no isolated island of turf, "
    "no cast shadow, no gradient, no border, no text, no seal, no signature."
)

JOBS = {
    'T-paddy2': (
        "A Japanese ukiyo-e woodblock print in the manner of Hiroshige: flooded rice paddies filling the whole image. "
        "About fourteen irregular plots across, slightly trapezoid and of uneven sizes, not a perfect checkerboard. "
        "Narrow raised earthen bunds of tan and sage, drawn with fine black keyline, some bunds gently curved. "
        "Most plots hold still pale sky-blue water with soft white bokashi sky reflections; some hold neat rows of young green rice shoots; "
        "a few are ripe gold. The paddies go to every edge. Flat colour, black keyline, visible woodgrain, cream paper. "
        + TOP,
        '1024x1024',
    ),
    'T-minehill': (
        "A Japanese ukiyo-e woodblock print in the manner of Hokusai, a terrain map patch seen from directly above. "
        "A rolling grassy hillside fills the entire image: sage green, olive and warm ochre grass with black contour keylines, "
        "the same hill language as a Hokusai atlas. Cut into the slope in the centre: a dark mine adit with a heavy timber frame, "
        "two low grey spoil heaps fanning downslope, a short wooden track, worn brown earth and pale rock around the workings. "
        "The hillside and grass continue to every edge so this can be printed into a map. No sky, no border, no people, no text. "
        "Flat colour, black keyline, visible woodgrain, cream paper. "
        + TOP,
        '1024x1024',
    ),
    'M-adit': (
        A_STYLE
        + "The subject: a small mine working without any grassy hill: a dark tunnel adit framed by a heavy timber post-and-lintel, "
        "two low spoil heaps of grey and ochre stones spilling to the sides, a small wooden ore cart on a short track. "
        "Low, compact, much wider than tall. Earth and spoil fade out at the edges into the magenta. No turf island, no grass mound. "
        + ANGLE,
        '1024x1024',
    ),
}


def bump(n=1):
    try:
        cur = int(open(COUNT_PATH).read().strip() or '0')
    except FileNotFoundError:
        cur = 0
    cur += n
    open(COUNT_PATH, 'w').write(str(cur))
    return cur


def gen(job):
    name, prompt, size = job
    path = f'{OUT}/{name}.png'
    if os.path.exists(path):
        return name, 'exists'
    key = os.environ.get('OPENAI_API_KEY')
    if not key:
        return name, 'FATAL missing OPENAI_API_KEY'
    body = json.dumps(dict(model='gpt-image-2', prompt=prompt, size=size, quality='medium', n=1)).encode()
    err = ''
    for attempt in range(3):
        req = urllib.request.Request(
            'https://api.openai.com/v1/images/generations',
            body,
            {'Authorization': f'Bearer {key}', 'Content-Type': 'application/json'},
        )
        try:
            with urllib.request.urlopen(req, timeout=600) as r:
                data = json.load(r)
            open(path, 'wb').write(base64.b64decode(data['data'][0]['b64_json']))
            n = bump()
            return name, f'ok call={n}'
        except urllib.error.HTTPError as e:
            msg = e.read().decode()[:400]
            if e.code in (401, 402, 403, 429) or 'billing' in msg.lower():
                return name, f'FATAL {e.code} {msg}'
            err = f'{e.code} {msg}'
        except Exception as e:
            err = repr(e)
        time.sleep(5 * (attempt + 1))
    return name, f'failed {err}'


def jobs():
    for k, (p, size) in JOBS.items():
        yield f'ukiyo-e-{k}', p, size


if __name__ == '__main__':
    sel = sys.argv[1:]
    js = [j for j in jobs() if not sel or any(s in j[0] for s in sel)]
    fatal = False
    with ThreadPoolExecutor(min(4, len(js) or 1)) as ex:
        for name, status in ex.map(gen, js):
            print(name, status, flush=True)
            if status.startswith('FATAL'):
                fatal = True
    if fatal:
        sys.exit(2)
