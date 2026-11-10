"""One-off test of the repaint approach: the painted map renderer lays out a region (where each terrain, coast, river
and resource is), and the image model repaints that layout as one coherent illustration in the style of the mockup.

Usage: OPENAI_API_KEY=... python3 repaint_test.py LAYOUT.png MOCKUP.jpg OUT.png
LAYOUT.png is a square crop of a renderer preview (for example from the HD layer tests' PAINTED_PREVIEW).
"""

import base64
import os
import sys

import requests

PROMPT = ('Repaint the first image as a finished map in exactly the style of the second image: the same woodblock-print '
          'technique, fine dark key-block lines, the same muted colours, the same dense hand-drawn detail of small trees, '
          'tufts, fields and hatching, as one coherent illustration seen from a high top-down angle. Keep the layout of '
          'the first image exactly: every coastline, lake, river, forest, mountain range, hill, desert, ice and grassland '
          'stays where it is, with the same shape and size, and the small objects (huts, animals, fields) stay in their '
          'places. Do not add cities, roads, units, flags, labels, text, a frame or a border.')


def main(layout, mockup, out):
    key = os.environ.get('OPENAI_API_KEY')
    if not key:
        sys.exit('Set OPENAI_API_KEY first.')
    files = [('image[]', (os.path.basename(p), open(p, 'rb'), 'image/jpeg' if p.lower().endswith('.jpg') else 'image/png'))
             for p in (layout, mockup)]
    r = requests.post('https://api.openai.com/v1/images/edits', headers={'Authorization': f'Bearer {key}'},
                      data={'model': 'gpt-image-2', 'prompt': PROMPT, 'size': '1024x1024', 'quality': 'high', 'n': '1'},
                      files=files, timeout=600)
    if r.status_code != 200:
        sys.exit(f'HTTP {r.status_code} {r.text[:300]}')
    with open(out, 'wb') as f:
        f.write(base64.b64decode(r.json()['data'][0]['b64_json']))
    print('written', out)


if __name__ == '__main__':
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    main(*sys.argv[1:])
