"""One-off test of the repaint approach: the painted map renderer lays out a region (where each terrain, coast, river
and resource is), and the image model repaints that layout as one coherent illustration in the style of the mockup.

Usage: OPENAI_API_KEY=... python3 repaint_test.py LAYOUT.png MOCKUP.jpg OUT.png
LAYOUT.png is a square crop of a renderer preview (for example from the HD layer tests' PAINTED_PREVIEW).
"""

import base64
import os
import sys

import requests

PROMPT = ('The first image is a flat colour plan of a region of a strategy game map: dark blue is deep sea, light blue '
          'is shallow coast, white is arctic ice, lilac is tundra, green is grassland, gold is plains, pale cream is '
          'desert, dark green is forest, bright green with palms is jungle, grey is mountains, olive is hills, dark olive '
          'is swamp, blue lines are rivers. Paint this region as a finished map in exactly the style of the second image: '
          'the same woodblock-print technique with fine dark key-block lines, the same muted colours, and the same dense, '
          'hand-drawn detail filling every part of the land: many small round trees, tufts, hatching, ridged hills, '
          'snow-capped peaks drawn as in the second image, dense forests of tree crowns, waves on the sea. Keep the '
          'layout of the plan: coastlines, lakes, rivers and the areas of each terrain stay where they are, at the same '
          'size. Do not add cities, roads, fields, units, flags, labels, text, a frame or a border.')


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
