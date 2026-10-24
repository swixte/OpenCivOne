"""Generates the raw art for the painted map with the OpenAI Images API, in the style of a concept image.

Each image is made with the concept image as a style reference: ground textures seen from above, and sprite
sheets of trees, jungle, hills and mountains on flat magenta, which painted_assets.py keys out and cuts up.

Usage: OPENAI_API_KEY=... python3 generate_painted.py CONCEPT.png OUT_DIR [NAME ...]
       then: python3 painted_assets.py OUT_DIR ../../assets/painted
"""

import base64
import os
import sys
from concurrent.futures import ThreadPoolExecutor

import requests

STYLE = ('Use the reference image only for its art style and colours: dark, moody, painterly, muted fantasy '
         'strategy map, light from the top left.')
TEXTURE = (STYLE + ' A seamless tileable ground texture seen straight from above, filling the entire square image edge '
           'to edge with even, flat lighting: no objects, no horizon, no vignette, no border, no text. The texture: ')
SHEET = ('Every object is isolated on a perfectly flat, uniform pure magenta (#FF00FF) background, with clear empty '
         'magenta space between objects so each can be cut out. No ground under them, no cast shadows, no text, no frame.')

PROMPTS = {
    'tex_grass': TEXTURE + 'wild meadow grass in olive green with soft patches of ochre and darker green, fine painterly brush strokes, like the open grassland in the reference.',
    'tex_plains': TEXTURE + 'dry golden-ochre prairie grass with a few darker tufts, like the yellow grass areas in the reference.',
    'tex_desert': TEXTURE + 'rippled sand dunes in warm tan and ochre with soft shaded troughs, like the desert in the reference.',
    'tex_tundra': TEXTURE + 'cold tundra: grey-olive moss and lichen with small stones and a few thin patches of old snow.',
    'tex_snow': TEXTURE + 'wind-swept snow and ice field, pale grey-white with cool blue-grey shadows in soft drifts.',
    'tex_ocean': TEXTURE + 'deep, dark open ocean water in navy and dark teal with long gentle swells and faint highlights, like the open sea in the reference.',
    'tex_shallow': TEXTURE + 'shallow coastal sea water in muted teal and green-grey, with sandy bottom faintly visible and soft ripples, like the shallows near the coast in the reference.',
    'tex_swamp': TEXTURE + 'dark boggy marsh: murky pools of dark water among olive moss, reeds and mud.',
    'tex_floor': TEXTURE + 'dark forest floor: moss, needles, roots and earth in very dark greens and browns.',
    'trees': STYLE + ' A sprite sheet of 20 separate dark green conifer fir trees seen from a high three-quarter top-down angle, exactly like the trees in the reference forest, varied sizes in a loose grid. ' + SHEET,
    'mountains': STYLE + ' A sprite sheet of 6 separate jagged rocky mountain peaks with snow on their tops, seen from a high three-quarter top-down angle exactly like the mountains in the reference, lit from the left with deep shade on the right, varied shapes, some single peaks and some small clusters of two or three peaks. ' + SHEET,
    'hills': STYLE + ' A sprite sheet of 9 separate low grassy hills and knolls with some exposed earth and rock, seen from a high three-quarter top-down angle exactly like the hills in the reference, lit from the left. Varied sizes. ' + SHEET,
    'jungle': STYLE + ' A sprite sheet of 16 separate dark, lush tropical broadleaf jungle trees and palm clusters seen from a high three-quarter top-down angle, in the same muted colours as the reference. Varied sizes in a loose grid. ' + SHEET,
}


def generate(key, concept, out, name):
    with open(concept, 'rb') as f:
        r = requests.post('https://api.openai.com/v1/images/edits', headers={'Authorization': f'Bearer {key}'},
                          data={'model': 'gpt-image-2', 'prompt': PROMPTS[name], 'size': '1024x1024', 'quality': 'medium', 'n': '1'},
                          files={'image[]': (os.path.basename(concept), f, 'image/png')}, timeout=600)
    if r.status_code != 200:
        return f'{name}: HTTP {r.status_code} {r.text[:300]}'
    with open(os.path.join(out, name + '.png'), 'wb') as f:
        f.write(base64.b64decode(r.json()['data'][0]['b64_json']))
    return f'{name}: done'


if __name__ == '__main__':
    key = os.environ.get('OPENAI_API_KEY')
    if not key or len(sys.argv) < 3:
        sys.exit(__doc__)
    concept, out = sys.argv[1], sys.argv[2]
    names = sys.argv[3:] or list(PROMPTS)
    os.makedirs(out, exist_ok=True)
    with ThreadPoolExecutor(6) as pool:
        for line in pool.map(lambda n: generate(key, concept, out, n), names):
            print(line)
