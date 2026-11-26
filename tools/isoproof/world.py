"""Mid-game look proof for the isometric map view: a generated world (continent, mountains, hills, rivers running
downhill to the sea, forests, jungle, swamp, desert, tundra and ice), four cities joined by a road network with paths to
hamlets, joined-up fields around the cities, units, and unexplored land under mist, drawn at the default zoom (about 14-16
tiles across the view).

Usage: python3 world.py THEME_DIR OUT.png
"""
import colorsys
import os
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', 'hdart'))
from painted_assets import cut_sprites, cut_object  # noqa: E402
import style  # noqa: E402

N = 40                      # world cells across and down
TW, TH = 96, 48             # diamond tile size at the default zoom
S = TW / 192                # sizes below are given for 192-pixel tiles
W, H = 1440, 900
PLAYERS = [(196, 58, 44), (52, 92, 170)]


def smooth(seed, scale):
    rng = np.random.default_rng(seed)
    small = rng.random((N // scale + 3, N // scale + 3))
    big = np.asarray(Image.fromarray((small * 255).astype(np.uint8)).resize(((N // scale + 3) * scale,) * 2, Image.BICUBIC), dtype=np.float64)
    return big[:N, :N] / 255


def make_world(seed=37):
    """The terrain letters of a generated world, its rivers, cities, roads and explored cells."""
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:N, 0:N]
    d = np.hypot((xx - N / 2) / (N / 2), (yy - N / 2) / (N / 2))
    elev = smooth(seed, 6) * 0.65 + smooth(seed + 1, 3) * 0.35 - d * 0.55
    moist = smooth(seed + 2, 7)
    land = elev > 0.08
    g = np.full((N, N), 'o')
    g[land] = 'g'
    g[land & (moist > 0.62)] = 'f'
    g[land & (moist > 0.72) & (yy > N * 0.55)] = 'j'
    g[land & (moist < 0.32)] = 'p'
    g[land & (moist < 0.22) & (yy > N * 0.5)] = 'd'
    g[land & (moist > 0.55) & (moist < 0.6) & (elev < 0.13)] = 's'
    g[land & (elev > 0.42)] = 'h'
    g[land & (elev > 0.52)] = 'm'
    g[land & (yy < N * 0.22)] = 't'
    g[land & (yy < N * 0.12)] = 'a'
    # rivers: from high ground, step downhill to the sea
    rivers = []
    springs = [tuple(p) for p in np.argwhere((g == 'h') | (g == 'm'))]
    rng.shuffle(springs)
    for sy, sx in springs[:12]:
        y, x = sy, sx
        path = [(x, y)]
        for _ in range(40):
            nbrs = [(y + dy, x + dx) for dy, dx in ((0, 1), (1, 0), (0, -1), (-1, 0)) if 0 <= y + dy < N and 0 <= x + dx < N]
            ny, nx = min(nbrs, key=lambda p: elev[p] + rng.uniform(0, 0.02))
            if elev[ny, nx] >= elev[y, x] or (nx, ny) in path:
                break
            y, x = ny, nx
            path.append((x, y))
            if g[y, x] == 'o':
                break
        if len(path) >= 6 and g[path[-1][1], path[-1][0]] == 'o':
            rivers.append(path)
        if len(rivers) == 3:
            break
    # cities on open land, well apart
    cities = []
    open_cells = [tuple(p[::-1]) for p in np.argwhere((g == 'g') | (g == 'p'))]
    rng.shuffle(open_cells)
    for c in open_cells:
        if all(max(abs(c[0] - o[0]), abs(c[1] - o[1])) >= 6 for o, _, _, _ in cities):
            cities.append((c, ['Kyoto', 'Nara', 'Osaka', 'Edo'][len(cities)], [7, 4, 5, 3][len(cities)], 0 if len(cities) < 3 else 1))
        if len(cities) == 4:
            break
    # roads: from each city to its nearest neighbour, by the shortest way over land (no roads across water)
    from collections import deque
    roads, joined = [], set()
    for c, _, _, _ in cities:
        others = sorted((o for o, _, _, _ in cities if o != c), key=lambda o: max(abs(o[0] - c[0]), abs(o[1] - c[1])))
        for o in others[:2]:
            if (c, o) in joined or (o, c) in joined:
                continue
            prev, q = {c: None}, deque([c])
            while q and o not in prev:
                x, y = q.popleft()
                for dx in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        n = (x + dx, y + dy)
                        if n not in prev and 0 <= n[0] < N and 0 <= n[1] < N and g[n[1], n[0]] not in 'om':
                            prev[n] = (x, y)
                            q.append(n)
            if o in prev:
                path, p = [], o
                while p is not None:
                    path.append(p)
                    p = prev[p]
                if len(path) < 14:
                    roads.append(path[::-1])
                    joined.add((c, o))
    # explored land: everything but a ragged band across the north-east
    ccx = np.mean([c[0] for c, _, _, _ in cities]); ccy = np.mean([c[1] for c, _, _, _ in cities])
    explored = np.hypot(xx - ccx, yy - ccy) < 15 + (smooth(seed + 5, 3) - 0.5) * 6
    return g, rivers, cities, roads, explored


def calm(tex, mean, contrast):
    lum = tex.mean(-1, keepdims=True)
    return np.clip(np.array(mean, dtype=np.float64) + (lum - lum.mean()) * contrast, 0, 255)


def pixel_noise(shape, scale, seed):
    rng = np.random.default_rng(seed)
    small = rng.random((shape[0] // scale + 2, shape[1] // scale + 2))
    return np.asarray(Image.fromarray((small * 255).astype(np.uint8)).resize((shape[1] + scale, shape[0] + scale), Image.BICUBIC),
                      dtype=np.float64)[:shape[0], :shape[1]] / 255


def main(theme, out):
    g, rivers, cities, roads, explored = make_world()

    def at(x, y):
        return g[y, x] if 0 <= x < N and 0 <= y < N else 'o'
    raw = os.path.join(theme, 'raw-iso')
    gdir = os.path.join(theme, 'hd', 'painted', 'ground')

    def seam(path, scale):
        img = Image.fromarray(style.seamless(Image.open(path).convert('RGB'), 1024)).convert('RGB')
        n = max(64, int(512 * scale))
        return np.asarray(img.resize((n, n), Image.LANCZOS), dtype=np.float64)
    load = lambda n: np.asarray(Image.open(os.path.join(gdir, n + '.png')).convert('RGB'), dtype=np.float64)
    # textures at the zoom's scale, so their marks keep the size of the objects' lines
    grass = seam(os.path.join(raw, 'iso_tex_grass.png'), 0.75)
    sand = seam(os.path.join(raw, 'iso_tex_sand.png'), 0.75) * np.array([1.0, 0.94, 0.80])      # warmer, towards ochre
    snow = calm(load('snow'), (236, 234, 224), 1.0)
    sea = calm(load('ocean'), (52, 98, 116), 0.28)
    shallow = calm(load('shallow'), (84, 142, 150), 0.35)
    ground_of = {'g': ('grass', (1.0, 1.0, 1.0)), 'p': ('grass', (1.10, 1.05, 0.82)), 'f': ('grass', (0.86, 0.94, 0.84)),
                 'j': ('grass', (0.86, 0.98, 0.82)), 'h': ('grass', (1.0, 0.97, 0.86)), 'm': ('grass', (0.96, 0.95, 0.92)),
                 't': ('grass', (1.02, 0.98, 1.04)), 's': ('grass', (0.80, 0.88, 0.82)), 'd': ('sand', (1, 1, 1)),
                 'a': ('snow', (1, 1, 1)), 'o': ('sea', (1, 1, 1))}

    ccx = np.mean([c[0] for c, _, _, _ in cities]) + 0.5; ccy = np.mean([c[1] for c, _, _, _ in cities]) + 0.5
    ox, oy = W / 2 - (ccx - ccy) * TW / 2, H / 2 - (ccx + ccy) * TH / 2

    def scr(x, y):
        return ox + (x - y) * TW / 2, oy + (x + y) * TH / 2
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float64)
    wob = 0.3
    u = (xx - ox) / (TW / 2) + ((pixel_noise((H, W), 50, 1) - 0.5) * wob * 2) + ((pixel_noise((H, W), 14, 3) - 0.5) * wob * 0.6)
    v = (yy - oy) / (TH / 2) + ((pixel_noise((H, W), 50, 2) - 0.5) * wob * 2) + ((pixel_noise((H, W), 14, 4) - 0.5) * wob * 0.6)
    gx, gy = (u + v) / 2, (v - u) / 2
    ix, iy = np.floor(gx).astype(int), np.floor(gy).astype(int)
    letters = sorted(ground_of)
    votes = np.zeros((H, W, len(letters)))
    lut = np.vectorize(at)
    for dj in (-1, 0, 1):
        for di in (-1, 0, 1):
            cxs, cys = ix + di, iy + dj
            d = np.sqrt((gx - (cxs + 0.5)) ** 2 + (gy - (cys + 0.5)) ** 2)
            w = np.clip(1.2 - d * 1.15, 0, None) ** 3
            lab = lut(np.clip(cxs, -1, N), np.clip(cys, -1, N))
            for k, ch in enumerate(letters):
                votes[..., k] += w * (lab == ch)
    label = np.array(letters)[votes.argmax(-1)]

    def sample(t):
        n = t.shape[0]
        return t[yy.astype(int) % n, xx.astype(int) % n]
    tex = {'grass': sample(grass), 'sand': sample(sand), 'snow': sample(snow), 'sea': sample(sea)}
    img = np.zeros((H, W, 3))
    for ch, (name, tint) in ground_of.items():
        m = label == ch
        img[m] = tex[name][m] * np.array(tint)
    kind = np.vectorize(lambda ch: ground_of[ch][0])(label)
    water = kind == 'sea'
    dist = ndimage.distance_transform_edt(water)
    band = np.clip(1 - (dist - 2) / 22, 0, 1)[..., None] * water[..., None]
    img = img * (1 - band) + sample(shallow) * band
    foam = water & (np.abs(dist - 4) < 1.1) & (pixel_noise((H, W), 7, 5) > 0.45)
    img[foam] = img[foam] * 0.35 + np.array([236, 232, 214]) * 0.65
    edge = np.zeros((H, W), dtype=bool)
    for dy_, dx_ in ((0, 1), (1, 0)):
        edge |= kind != np.roll(np.roll(kind, -dy_, 0), -dx_, 1)
    img[edge] = img[edge] * 0.25 + np.array([40, 34, 28]) * 0.75
    landd = ndimage.distance_transform_edt(~water)
    beach = (~water) & (landd < 3.5) & ~edge
    img[beach] = img[beach] * 0.55 + np.array([214, 198, 150]) * 0.45
    canvas = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).convert('RGBA')
    dr = ImageDraw.Draw(canvas)
    rng = np.random.default_rng(7)

    def spline(points, steps=10):
        ctrl = [points[0]] + list(points) + [points[-1]]
        pts = []
        for i in range(1, len(ctrl) - 2):
            p0, p1, p2, p3 = ctrl[i - 1], ctrl[i], ctrl[i + 1], ctrl[i + 2]
            for t in np.linspace(0, 1, steps, endpoint=False):
                t2, t3 = t * t, t * t * t
                pts.append(tuple(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)))
        pts.append(tuple(ctrl[-2]))
        return pts

    def wind(cells, swing):
        pts = []
        for k, (x, y) in enumerate(cells):
            pts.append(np.array(scr(x + 0.5 + rng.uniform(-0.15, 0.15), y + 0.5 + rng.uniform(-0.15, 0.15))))
            if k + 1 < len(cells):
                nx, ny = cells[k + 1]
                m = np.array(scr((x + nx) / 2 + 0.5, (y + ny) / 2 + 0.5))
                side = np.array([-(m - pts[-1])[1], (m - pts[-1])[0]])
                pts.append(m + side / (np.linalg.norm(side) + 1e-9) * rng.uniform(-swing, swing))
        return spline(pts)

    # fields first (they lie on the ground under everything): joined-up patches around each city, lighter and varied
    sets = {n: cut_sprites(Image.open(os.path.join(raw, f'iso_{n}.png'))) for n in
            ('trees', 'peaks', 'hills2', 'jungle', 'town', 'tufts', 'tundra', 'reeds', 'ice', 'dunes', 'plots', 'hamlet')}

    def lighten(spr):
        a = np.asarray(spr.convert('RGBA')).astype(np.float64)
        rgb = a[..., :3] / 255
        hsv = np.vectorize(colorsys.rgb_to_hsv)(rgb[..., 0], rgb[..., 1], rgb[..., 2])
        h, s_, v = hsv
        h = np.where((h < 0.14), h + 0.07, h)           # orange squares towards yellow-green
        v = np.clip(v * 1.22 + 0.04, 0, 1)
        s_ = s_ * 0.9
        r, g_, b = np.vectorize(colorsys.hsv_to_rgb)(h, s_, v)
        a[..., :3] = np.dstack([r, g_, b]) * 255
        return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), 'RGBA')
    plots = [lighten(p) for p in sets['plots']]
    city_cells = {c for c, _, _, _ in cities}
    road_cells = {p for r in roads for p in r}
    river_cells = {p for r in rivers for p in r}
    field_cells = set()
    for (cx_, cy_), _, size, _ in cities:
        radius = 2 if size > 4 else 1
        for y in range(cy_ - radius, cy_ + radius + 1):
            for x in range(cx_ - radius, cx_ + radius + 1):
                if (x, y) not in city_cells and at(x, y) in 'gp' and (x, y) not in river_cells and explored[min(max(y, 0), N - 1), min(max(x, 0), N - 1)]:
                    if rng.random() < 0.8:
                        field_cells.add((x, y))
    for x, y in sorted(field_cells, key=lambda p: p[0] + p[1]):
        px, py = scr(x + 0.5, y + 0.5)
        spr = plots[int(rng.integers(len(plots)))]
        if rng.random() < 0.5:
            spr = spr.transpose(Image.FLIP_LEFT_RIGHT)
        # a little larger than the tile, so neighbouring fields join without a gap
        sp = spr.resize((int(TW * 1.08), int(TH * 1.08)), Image.LANCZOS)
        canvas.alpha_composite(sp, (int(px - sp.width / 2), int(py - sp.height / 2)))
    dr = ImageDraw.Draw(canvas)
    # rivers, then roads over them with a bridge where they cross
    river_lines = []
    for path in rivers:
        line = wind(path, 10 * S * 2)
        river_lines.append(line)
        dr.line(line, fill=(214, 204, 168, 255), width=int(15 * S * 1.6), joint='curve')
        dr.line(line, fill=(40, 44, 52, 255), width=int(11 * S * 1.6), joint='curve')
        dr.line(line, fill=(86, 134, 160, 255), width=int(7 * S * 1.6), joint='curve')
    for path in roads:
        line = wind(path, 6 * S * 2)
        dr.line(line, fill=(70, 56, 36, 255), width=int(9 * S * 1.6), joint='curve')
        dr.line(line, fill=(222, 204, 158, 255), width=int(6 * S * 1.6), joint='curve')
        for cell in path:
            if cell in river_cells:
                bx, by = scr(cell[0] + 0.5, cell[1] + 0.5)
                bw, bh = 16, 8
                dr.polygon([(bx - bw, by), (bx, by - bh), (bx + bw, by), (bx, by + bh)], fill=(150, 104, 62, 255), outline=(40, 34, 28, 255))
                dr.line([(bx - bw + 3, by), (bx + bw - 3, by)], fill=(110, 76, 44, 255), width=1)

    ground_items, items = [], []

    def pick(name):
        lst = sets[name]
        return lst[int(rng.integers(len(lst)))]

    def inside(x, y, m=0.15):
        return scr(x + rng.uniform(m, 1 - m), y + rng.uniform(m, 1 - m))

    def is_explored(x, y):
        return 0 <= x < N and 0 <= y < N and explored[y, x]
    hamlets = []
    for y in range(-2, N + 2):
        for x in range(-2, N + 2):
            ch = at(x, y)
            if (x, y) in city_cells or not is_explored(min(max(x, 0), N - 1), min(max(y, 0), N - 1)):
                continue
            if (x, y) in field_cells:
                if rng.random() < 0.3:
                    px, py = inside(x, y, 0.3)
                    items.append((py, px, pick('hamlet'), 64 * S * 1.6))
                    hamlets.append((x, y))
                continue
            if ch == 'f':
                for _ in range(10):
                    px, py = inside(x, y, 0.04)
                    items.append((py, px, pick('trees'), 160 * S))
            elif ch == 'j':
                for _ in range(8):
                    px, py = inside(x, y, 0.04)
                    items.append((py, px, pick('jungle'), 160 * S))
            elif ch == 'm':
                px, py = scr(x + 0.55, y + 0.6)
                items.append((py + 10 * S, px, pick('peaks'), 240 * S))
            elif ch == 'h':
                for _ in range(2):
                    px, py = inside(x, y, 0.25)
                    items.append((py + 8 * S, px, pick('hills2'), 145 * S))
            elif ch == 'd':
                for _ in range(int(rng.integers(2, 4))):
                    px, py = inside(x, y)
                    items.append((py, px, pick('dunes'), 90 * S))
            elif ch == 's':
                for _ in range(int(rng.integers(6, 9))):
                    px, py = inside(x, y)
                    items.append((py, px, pick('reeds'), 40 * S))
            elif ch == 't':
                for _ in range(int(rng.integers(5, 8))):
                    px, py = inside(x, y)
                    items.append((py, px, pick('tundra'), 30 * S * 1.4))
            elif ch == 'a':
                for _ in range(int(rng.integers(3, 5))):
                    px, py = inside(x, y)
                    items.append((py, px, pick('ice'), 70 * S))
            elif ch in 'gp':
                for _ in range(int(rng.integers(2, 5))):
                    px, py = inside(x, y, 0.1)
                    r = rng.random()
                    if r < 0.35:
                        items.append((py, px, sets['trees'][int(rng.integers(0, 4))], 56 * S * 1.4))
                    elif r < 0.75:
                        items.append((py, px, pick('tufts'), 30 * S * 1.5))
                    elif r < 0.93:
                        items.append((py, px, pick('tundra'), 28 * S * 1.5))
                    else:
                        items.append((py, px, pick('hamlet'), 64 * S * 1.6))
                        hamlets.append((x, y))
            # stragglers: woods spill a few clumps onto open land beside them, so their edge does not follow the tiles
            if ch in 'gpt' and any(at(x + dx, y + dy) in 'fj' for dx in (-1, 0, 1) for dy in (-1, 0, 1)):
                for _ in range(int(rng.integers(1, 4))):
                    px, py = inside(x, y, 0.05)
                    items.append((py, px, pick('trees') if any(at(x + dx, y + dy) == 'f' for dx in (-1, 0, 1) for dy in (-1, 0, 1)) else pick('jungle'), 150 * S))
            # scrub along the desert's border, and drifts and rocks along the ice's
            if ch in 'gp' and any(at(x + dx, y + dy) == 'd' for dx in (-1, 0, 1) for dy in (-1, 0, 1)):
                for _ in range(int(rng.integers(2, 4))):
                    px, py = inside(x, y)
                    items.append((py, px, pick('dunes'), 60 * S))
            if ch == 't' and any(at(x + dx, y + dy) == 'a' for dx in (-1, 0, 1) for dy in (-1, 0, 1)):
                for _ in range(int(rng.integers(2, 4))):
                    px, py = inside(x, y)
                    items.append((py, px, pick('ice'), 50 * S))
    # paths from the roads out to the hamlets
    dr = ImageDraw.Draw(canvas)
    for hx, hy in hamlets[::2]:
        near = min((p for r in roads for p in r), key=lambda p: max(abs(p[0] - hx), abs(p[1] - hy)), default=None)
        if near is None or max(abs(near[0] - hx), abs(near[1] - hy)) > 2:
            continue
        a, b = np.array(scr(near[0] + 0.5, near[1] + 0.5)), np.array(scr(hx + 0.5, hy + 0.5))
        m = (a + b) / 2 + np.array([rng.uniform(-8, 8), rng.uniform(-4, 4)])
        dr.line(spline([a, m, b], 8), fill=(186, 166, 120, 255), width=3, joint='curve')

    settler = cut_object(Image.open(os.path.join(theme, 'raw', 'unit_settlers.png')))
    warrior = cut_object(Image.open(os.path.join(theme, 'raw', 'unit_militia.png')))
    horse = cut_object(Image.open(os.path.join(theme, 'raw', 'unit_horsemen.png'))) if os.path.exists(os.path.join(theme, 'raw', 'unit_horsemen.png')) else warrior
    units = []
    for k, ((cx_, cy_), name, size, owner) in enumerate(cities):
        tx, ty = scr(cx_ + 0.5, cy_ + 0.5)
        items.append((ty + 30 * S, tx, sets['town'][k % len(sets['town'])], TW * 1.25 * (1.0 if size > 4 else 0.85)))
        spr = [settler, warrior, horse, warrior][k]
        ux_, uy_ = cx_ + [1, -1, 1, 0][k], cy_ + [1, 1, -1, 2][k]
        if at(ux_, uy_) != 'o':
            units.append((spr, ux_, uy_, owner))
    for spr, x, y, owner in units:
        ux, uy = scr(x + 0.5, y + 0.5)
        items.append((uy + 20 * S, ux, spr, None))

    widest = {id(spr): max(o.width for o in lst) for lst in sets.values() for spr in lst}
    for base, x, spr, width in sorted(items, key=lambda t: t[0]):
        k = (112 * S * 1.2 / spr.height) if width is None else width / widest.get(id(spr), spr.width)
        sp = spr.resize((max(1, int(spr.width * k)), max(1, int(spr.height * k))), Image.LANCZOS)
        canvas.alpha_composite(sp, (int(x - sp.width / 2), int(base - sp.height)))

    # unexplored land: dark paper with bands of mist, over everything, with a ragged edge
    fog = ~np.vectorize(lambda a, b: is_explored(min(max(a, 0), N - 1), min(max(b, 0), N - 1)))(np.clip(ix, -1, N), np.clip(iy, -1, N))
    fog = ndimage.binary_opening(fog, iterations=2)
    mist_rows = ((yy + np.sin(xx / 70) * 9) % 46 < 15) & (pixel_noise((H, W), 60, 9) > 0.45)
    fog_rgb = np.where(mist_rows[..., None], np.array([52, 52, 64.]), np.array([34, 34, 42.]))
    cur = np.asarray(canvas, dtype=np.float64)
    soft = ndimage.gaussian_filter(fog.astype(float), 2.5)
    cur[..., :3] = cur[..., :3] * (1 - soft[..., None]) + fog_rgb * soft[..., None]
    canvas = Image.fromarray(np.clip(cur, 0, 255).astype(np.uint8), 'RGBA')

    def flag(x, y, size, colour, pole=1.9):
        d = ImageDraw.Draw(canvas)
        d.line([(x, y), (x, y - size * pole)], fill=(40, 34, 28, 255), width=max(2, int(size // 7)))
        top = y - size * pole
        d.polygon([(x, top), (x + size * 1.2, top + size * 0.12), (x + size, top + size * 0.45), (x + size * 1.2, top + size * 0.8), (x, top + size * 0.7)],
                  fill=colour + (255,), outline=(40, 34, 28, 255))
    for spr, x, y, owner in units:
        if not is_explored(x, y):
            continue
        ux, uy = scr(x + 0.5, y + 0.5)
        flag(ux - 16, uy - 34, 11, PLAYERS[owner])
    try:
        font = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf', 15)
    except OSError:
        font = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf', 15)
    d = ImageDraw.Draw(canvas)
    for (cx_, cy_), name, size, owner in cities:
        if not is_explored(cx_, cy_):
            continue
        tx, ty = scr(cx_ + 0.5, cy_ + 0.5)
        flag(tx + 18, ty - 40, 16, PLAYERS[owner], 2.3)
        nw = d.textlength(name, font=font)
        px0, py0 = tx - (nw + 32) / 2, ty + 24
        d.rectangle([px0, py0, px0 + 22, py0 + 22], fill=PLAYERS[owner] + (255,), outline=(40, 34, 28, 255), width=2)
        d.text((px0 + 11, py0 + 11), str(size), fill=(244, 236, 216, 255), font=font, anchor='mm')
        d.rectangle([px0 + 22, py0, px0 + 30 + nw + 6, py0 + 22], fill=(240, 230, 204, 255), outline=(40, 34, 28, 255), width=2)
        d.text((px0 + 28, py0 + 11), name, fill=(40, 34, 28, 255), font=font, anchor='lm')
    canvas.convert('RGB').save(out)
    print('rivers', len(rivers), 'roads', len(roads), 'cities', len(cities), 'hamlets', len(hamlets))


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
