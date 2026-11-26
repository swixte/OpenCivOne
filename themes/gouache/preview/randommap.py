"""A random Civ1-style world painted in the theme: continents, climate bands, ridges and rivers, then towns and units
placed on it. Usage: python randommap.py SEED [W H]  (writes random-SEED-scene.txt, -engine.png, -map.jpg, -zoom.jpg)"""
import os
import random
import subprocess
import sys
import numpy as np
from PIL import Image

SEED = int(sys.argv[1]) if len(sys.argv) > 1 else 1
W, H = (int(sys.argv[2]), int(sys.argv[3])) if len(sys.argv) > 3 else (34, 22)
rng = random.Random(SEED)
np.random.seed(SEED)


def noise(scale, octaves=4):
    """Smooth value noise on the W x H grid, 0..1."""
    out = np.zeros((H, W))
    amp, total = 1.0, 0.0
    for o in range(octaves):
        n = max(2, int(scale * 2 ** o))
        g = Image.fromarray((np.random.rand(n, n) * 255).astype('uint8')).resize((W, H), Image.BICUBIC)
        out += amp * np.asarray(g, float) / 255
        total += amp
        amp *= 0.5
    return out / total


# land: noise pulled down at the edges so the world is islands and continents in ocean, about 40% land
yy, xx = np.mgrid[0:H, 0:W]
edge = np.minimum.reduce([xx, yy, W - 1 - xx, H - 1 - yy]) / 3
height = noise(3) - 0.35 * np.clip(1 - edge, 0, 1)
land = height > np.quantile(height, 0.6)
land[[0, -1], :] = land[:, [0, -1]] = False
# climate: latitude for temperature (poles at top and bottom), noise for wetness and for ridges
lat = np.abs(yy - (H - 1) / 2) / ((H - 1) / 2)
temp = 1 - lat + 0.25 * (noise(4) - 0.5)
wet = noise(5)
ridge = 1 - np.abs(noise(4, 3) * 2 - 1)
grid = np.full((H, W), 'o')
for y in range(H):
    for x in range(W):
        if not land[y, x]:
            continue
        t, w, r = temp[y, x], wet[y, x], ridge[y, x]
        if t < 0.12:
            c = 'a'
        elif t < 0.25:
            c = 't'
        elif r > 0.94:
            c = 'm'
        elif r > 0.86:
            c = 'h'
        elif t > 0.75:
            c = 'd' if w < 0.38 else 'p' if w < 0.48 else 'g' if w < 0.58 else 'j' if w < 0.7 else 's'
        else:
            c = 'p' if w < 0.42 else 'g' if w < 0.55 else 'f' if w < 0.7 else 's' if w > 0.8 else 'g'
        grid[y, x] = c
# rivers: from a few hills or mountains, always a step nearer the sea, through the low land on the way
dist = np.where(grid == 'o', 0, 999)
for _ in range(W + H):
    for y in range(1, H - 1):
        for x in range(1, W - 1):
            if dist[y, x]:
                dist[y, x] = min(dist[y, x], 1 + min(dist[y - 1, x], dist[y + 1, x], dist[y, x - 1], dist[y, x + 1]))
starts = [(y, x) for y in range(H) for x in range(W) if grid[y, x] in 'hm' and dist[y, x] >= 3]
rng.shuffle(starts)
for y, x in starts[:4]:
    while dist[y, x] > 0:
        nb = [(y + dy, x + dx) for dy, dx in ((0, 1), (1, 0), (0, -1), (-1, 0)) if dist[y + dy, x + dx] < dist[y, x]]
        y, x = rng.choice(nb)
        if grid[y, x] in 'pgdfjs':
            grid[y, x] = 'r'
rows = [''.join(r) for r in grid]
scene = f'random-{SEED}-scene.txt'
open(scene, 'w').write('\n'.join(rows) + '\n')

# towns on good land, at least three cells apart, two players; units on land and ships at sea by the coast
names = ['Babylon', 'Ur', 'Thebes', 'Kish', 'Mari', 'Nineveh', 'Assur', 'Uruk', 'Lagash', 'Eridu', 'Memphis', 'Sidon',
         'Tyre', 'Byblos', 'Larsa', 'Akkad']
good = [(x, y) for y in range(1, H - 1) for x in range(1, W - 1) if grid[y, x] in 'pgrdf' and temp[y, x] > 0.3]
rng.shuffle(good)
towns = []
for x, y in good:
    if all(max(abs(x - a), abs(y - b)) >= 3 for a, b, *_ in towns) and len(towns) < 10:
        step = rng.choice([0, 1, 1, 2, 2, 3, 3, 4, 5]) if towns else 5
        size = [1, 3, 5, 7, 10, 14][step] + rng.randint(0, 1)
        towns.append((x, y, 'blue' if x < W / 2 else 'red', names[len(towns)], size, step, step >= 2 and rng.random() < 0.5))
taken = {(a, b) for a, b, *_ in towns}
landu = ['settlers', 'militia', 'phalanx', 'legion', 'knights', 'chariot', 'catapult']
units = []
spots = [(x, y) for y in range(1, H - 1) for x in range(1, W - 1) if grid[y, x] != 'o' and (x, y) not in taken
         and any(max(abs(x - a), abs(y - b)) <= 2 for a, b, *_ in towns)]
rng.shuffle(spots)
for x, y in spots[:10]:
    if all(max(abs(x - a), abs(y - b)) >= 2 for a, b, *_ in units):
        units.append((x, y, rng.choice(landu), 'blue' if x < W / 2 else 'red', False))
coast = [(x, y) for y in range(1, H - 1) for x in range(1, W - 1) if grid[y, x] == 'o'
         and any(grid[y + dy, x + dx] != 'o' for dy in (-1, 0, 1) for dx in (-1, 0, 1))]
rng.shuffle(coast)
for x, y in coast[:3]:
    units.append((x, y, rng.choice(['trireme', 'sail']), 'blue' if x < W / 2 else 'red', False))
units[0] = units[0][:4] + (True,)

engine = f'random-{SEED}-engine.png'
env = dict(os.environ, PAINTED_ART='../hd/painted', PAINTED_PREVIEW=os.path.abspath(engine), PAINTED_SCENE=os.path.abspath(scene),
           PAINTED_CITIES=';'.join(f'{x},{y},{s}' for x, y, _, _, _, s, _ in towns), DOTNET_ROLL_FORWARD='Major')
out = subprocess.run(['dotnet', 'run', '--project', '../../../tests/HDLayerTests', '-c', 'Release', '--no-build'],
                     env=env, capture_output=True, text=True)
print([l for l in out.stdout.splitlines() if 'preview' in l] or out.stdout[-500:] + out.stderr[-500:])

sys.argv = [sys.argv[0], engine, f'random-{SEED}-']
import compose
compose.TOWNS, compose.UNITS = towns, units
compose.scene(1).save(f'random-{SEED}-map.jpg', quality=90)
print('\n'.join(rows))
