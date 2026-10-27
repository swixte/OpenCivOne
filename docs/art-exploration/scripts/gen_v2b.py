"""Generate tileable paddy and forest-mine patches. Key via env only; never print it."""
import base64, json, os, sys, time, urllib.request, urllib.error

OUT = '/tmp/mood-comp/raw'
COUNT_PATH = '/tmp/mood-comp/api-count.txt'
os.makedirs(OUT, exist_ok=True)

TOP = (
    "Seen straight down from directly above like a map, orthographic, no horizon, no perspective, no surrounding sky. "
    "The pattern fills the entire image evenly edge to edge, no border, no frame, no buildings, no people, "
    "no large trees except where asked, no animals, no text, no seals, no signature. "
)

JOBS = {
    'T-paddy3': (
        "A seamless tileable Japanese ukiyo-e woodblock print in the manner of Hiroshige: an all-over repeating pattern of flooded rice paddies. "
        "About eighteen irregular four-sided plots of slightly different sizes, not a perfect checkerboard, not a kaleidoscope, not mirrored, no radial centre, no diamond meeting in the middle. "
        "Narrow raised earthen bunds of tan and sage with fine black keyline, some bunds gently curved. "
        "Most plots hold still pale sky-blue water with soft white cloud bokashi reflections; some hold neat rows of young green rice shoots; a few are ripe gold. "
        "The paddies continue through every edge so the pattern wraps: left edge matches right edge, top matches bottom. "
        "Even all-over repeat, no unique focal point. Flat colour, black keyline, visible woodgrain, cream paper. "
        + TOP,
        '1024x1024',
    ),
    'T-paddy4': (
        "A seamless tileable Japanese ukiyo-e woodblock print in the manner of Hiroshige: flooded rice paddies as a wrapping map texture. "
        "Twelve to sixteen irregular plots, roughly rectangular but uneven, with raised earthen bunds. "
        "Flooded plots reflect a pale morning sky (cream clouds on light blue); other plots show rows of green rice; one or two are harvest gold. "
        "Not mirrored, not a four-way kaleidoscope, not a radial design. Edges must tile: pattern continues off every side. "
        "Flat colour, black keyline, woodgrain, cream paper. "
        + TOP,
        '1024x1024',
    ),
    'T-mineforest': (
        "A Japanese ukiyo-e woodblock print in the manner of Hokusai, a terrain map patch seen from directly above. "
        "Dense dark-green pine clumps with black keylines and ochre grass between, filling the whole image to every edge like a Hokusai atlas forested hill. "
        "Cut into the slope, large and obvious in the centre: a mine that belongs to the hillside — a dark adit mouth with a heavy timber post-and-lintel, "
        "two grey-ochre spoil heaps fanning downslope, a short wooden track, pale broken rock and worn brown earth scarring the hill. "
        "The working fills about the middle third of the patch so it reads at small size. Forest continues around the scar to every edge. "
        "No sky, no ore cart close-up, no people, no text, no sticker, no circular island of grass. Flat colour, black keyline, woodgrain, cream paper. "
        + TOP,
        '1024x1024',
    ),
    'T-mineforest2': (
        "A Japanese ukiyo-e woodblock print, top-down map terrain. A steep forested hillside of dark green trees and olive grass, black contour keylines, filling every edge. "
        "A hillside mine cut: the trees open into a pale earth and rock scar, timber-framed adit in the cut face, grey spoil dumped down the slope, worn brown path. "
        "The scar is irregular and printed into the hill, not a round icon. Forest and grass surround it and continue to the edges. "
        "No sky, no people, no text, no magenta. Flat colour, black keyline, woodgrain, cream paper. "
        + TOP,
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


def gen(name, prompt, size):
    path = f'{OUT}/ukiyo-e-{name}.png'
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


if __name__ == '__main__':
    sel = sys.argv[1:]
    jobs = [(k, p, s) for k, (p, s) in JOBS.items() if not sel or any(x in k for x in sel)]
    fatal = False
    from concurrent.futures import ThreadPoolExecutor
    with ThreadPoolExecutor(min(4, len(jobs) or 1)) as ex:
        futs = [ex.submit(gen, n, p, s) for n, p, s in jobs]
        for f in futs:
            name, status = f.result()
            print(name, status, flush=True)
            if status.startswith('FATAL'):
                fatal = True
    if fatal:
        sys.exit(2)
