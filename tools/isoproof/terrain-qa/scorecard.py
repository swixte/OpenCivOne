"""Terrain QA scorecard: runs the data checks of the terrain QA framework over the fixture worlds (dumps written by the
HD layer tests with ISO_GENWORLDS) and writes one markdown page: every check, per generator mode, worst case and median
across the worlds, the target and PASS/FAIL.

Usage: python3 scorecard.py DUMPS_DIR OUT.md [COMMIT]
"""

import json
import os
import statistics
import sys

DESERT, PLAINS, GRASS, FOREST, HILLS, MOUNTAINS, TUNDRA, ARCTIC, SWAMP, JUNGLE, WATER, RIVER = range(12)
NAMES = ['Desert', 'Plains', 'Grassland', 'Forest', 'Hills', 'Mountains', 'Tundra', 'Arctic', 'Swamp', 'Jungle', 'Water', 'River']
LAND = [t for t in range(12) if t != WATER]
W, H = 80, 50
FOUR = ((1, 0), (-1, 0), (0, 1), (0, -1))
EIGHT = FOUR + ((1, 1), (1, -1), (-1, 1), (-1, -1))


def load(path):
    return [[int(c.split(':')[0]) for c in line.split()] for line in open(path)]


def regions(g, same, steps=FOUR):
    """Connected regions: lists of cells, grouping neighbouring cells for which same(a, b) holds."""
    seen, out = set(), []
    for y in range(H):
        for x in range(W):
            if (x, y) in seen:
                continue
            seen.add((x, y))
            stack, cells = [(x, y)], []
            while stack:
                cx, cy = stack.pop()
                cells.append((cx, cy))
                for dx, dy in steps:
                    nx, ny = (cx + dx) % W, cy + dy
                    if 0 <= ny < H and (nx, ny) not in seen and same(g[cy][cx], g[ny][nx]):
                        seen.add((nx, ny))
                        stack.append((nx, ny))
            out.append((g[y][x], cells))
    return out


def neighbours(g, x, y, steps=EIGHT):
    return [g[y + dy][(x + dx) % W] for dx, dy in steps if 0 <= y + dy < H]


def latitude(y):
    return abs(y - (H - 1) / 2) / ((H - 1) / 2)


def measure(g):
    """Every check of sections 2A to 2D for one world: name -> value."""
    m = {}
    regs = regions(g, lambda a, b: a == b)
    by_type = {t: [len(c) for tt, c in regs if tt == t] for t in LAND}
    land_tiles = sum(1 for row in g for t in row if t != WATER)

    # A. coherence
    for t in LAND:
        sizes = by_type[t]
        if not sizes or t == RIVER:
            continue
        m[f'A1 median region size: {NAMES[t]}'] = statistics.median(sizes)
        m[f'A2 single-tile regions: {NAMES[t]} (%)'] = 100 * sum(1 for s in sizes if s == 1) / len(sizes)
    small = sum(len(c) for t, c in regs if t not in (WATER, RIVER) and len(c) <= 2)
    m['A3 land tiles in regions of 1-2 tiles (%)'] = 100 * small / max(1, land_tiles)

    # B. climate sense
    m['B1 arctic tiles more than 3 rows from a pole'] = sum(1 for y in range(H) for x in range(W) if g[y][x] == ARCTIC and 3 <= y <= H - 4)
    m['B2 desert or jungle beyond 60% latitude'] = sum(1 for y in range(H) for x in range(W) if g[y][x] in (DESERT, JUNGLE) and latitude(y) > 0.6)
    bad = 0
    for y in range(H):
        for x in range(W):
            n = neighbours(g, x, y)
            if g[y][x] == JUNGLE and (DESERT in n or ARCTIC in n):
                bad += 1
            if g[y][x] == DESERT and (ARCTIC in n or SWAMP in n):
                bad += 1
    m['B3 forbidden neighbours (jungle by desert/arctic, desert by arctic/swamp)'] = bad
    mean_lat = {t: statistics.mean([latitude(y) for y in range(H) for x in range(W) if g[y][x] == t] or [0]) for t in LAND}
    order = [ARCTIC, TUNDRA, FOREST, (GRASS, PLAINS), (DESERT, JUNGLE)]
    values = [max(mean_lat[a] for a in o) if isinstance(o, tuple) else mean_lat[o] for o in order]
    lows = [min(mean_lat[a] for a in o) if isinstance(o, tuple) else mean_lat[o] for o in order]
    m['B4 latitude ordering arctic > tundra > forest > grass/plains > desert/jungle (steps out of order)'] = sum(
        1 for i in range(len(order) - 1) if lows[i] <= values[i + 1])

    # C. relief: ranges are mountains joined across diagonals too
    ranges = [c for t, c in regions(g, lambda a, b: a == b == MOUNTAINS, EIGHT) if t == MOUNTAINS]
    mtn = sum(len(c) for c in ranges)
    m['C1 mountain tiles in ranges of 3 or more (%)'] = 100 * sum(len(c) for c in ranges if len(c) >= 3) / max(1, mtn)
    with_hills = sum(1 for c in ranges if any(HILLS in neighbours(g, x, y) for x, y in c))
    m['C2 mountain ranges with hills beside them (%)'] = 100 * with_hills / max(1, len(ranges))

    # D. rivers
    systems = [c for t, c in regions(g, lambda a, b: a == b == RIVER) if t == RIVER]
    reach = sum(1 for c in systems if any(WATER in neighbours(g, x, y, FOUR) for x, y in c))
    m['D1 river systems reaching the sea or a lake (%)'] = 100 * reach / max(1, len(systems))
    sources = [(x, y) for c in systems for x, y in c if sum(1 for n in neighbours(g, x, y, FOUR) if n == RIVER) <= 1
               and WATER not in neighbours(g, x, y, FOUR)]
    m['D2 river sources beside hills or mountains (%)'] = 100 * sum(
        1 for x, y in sources if HILLS in neighbours(g, x, y) or MOUNTAINS in neighbours(g, x, y)) / max(1, len(sources))
    m['D3 junctions joining more than three channels'] = sum(
        1 for c in systems for x, y in c if sum(1 for n in neighbours(g, x, y, FOUR) if n == RIVER) > 3)
    m['D4 river tiles within 3 rows of a pole'] = sum(1 for y in range(H) for x in range(W) if g[y][x] == RIVER and (y < 3 or y > H - 4))
    return m


# the classic game's yield of each terrain without improvements: food, shields, trade
YIELD = {DESERT: (0, 1, 0), PLAINS: (1, 1, 0), GRASS: (2, 0, 0), FOREST: (1, 2, 0), HILLS: (1, 0, 0), MOUNTAINS: (0, 1, 0),
         TUNDRA: (1, 0, 0), ARCTIC: (0, 0, 0), SWAMP: (1, 0, 0), JUNGLE: (1, 0, 0), RIVER: (2, 0, 1)}


def load_features(path):
    return [[int(c.split(':')[1]) for c in line.split()] for line in open(path)]


def balance(classic, smoothed, cf, sf):
    """Section E: the smoothed world against its classic twin. Name -> difference (0 or a percentage)."""
    m = {}
    land = lambda g: [(x, y) for y in range(H) for x in range(W) if g[y][x] != WATER]
    m['E1 land tiles changed (count)'] = abs(len(land(classic)) - len(land(smoothed)))
    coast = lambda g: sum(1 for x, y in land(g) if WATER in neighbours(g, x, y, FOUR))
    m['E2 coastline tiles changed (count)'] = abs(coast(classic) - coast(smoothed))
    rivers = lambda g: {(x, y) for y in range(H) for x in range(W) if g[y][x] == RIVER}
    m['E3 river tiles changed (count)'] = len(rivers(classic) ^ rivers(smoothed))
    n = max(1, len(land(classic)))
    share = max(abs(sum(row.count(t) for row in classic) - sum(row.count(t) for row in smoothed)) * 100 / n for t in LAND)
    m['E4 largest change in a terrain share (points)'] = share
    for i, what in enumerate(('food', 'shields', 'trade')):
        total = lambda g: sum(YIELD[g[y][x]][i] for x, y in land(g))
        a, b = total(classic), total(smoothed)
        m[f'E5 total {what} changed (%)'] = abs(a - b) * 100 / max(1, a)
    for bit, what in ((1, 'special resources'), (2, 'huts')):
        count = lambda f: sum(1 for row in f for v in row if v & bit)
        a, b = count(cf), count(sf)
        m[f'E6 {what} changed (%)'] = abs(a - b) * 100 / max(1, a)
    continents = lambda g: sum(1 for t, c in regions(g, lambda p, q: (p == WATER) == (q == WATER)) if t != WATER)
    m['E7 continents changed (count)'] = abs(continents(classic) - continents(smoothed))
    return m


def target(name):
    """(description, test) for a check."""
    if name.startswith(('E1', 'E2', 'E3', 'E7')):
        return '0', lambda v: v == 0
    if name.startswith('E4'):
        return '<= 2', lambda v: v <= 2
    if name.startswith('E5'):
        return '<= 3', lambda v: v <= 3
    if name.startswith('E6'):
        return '<= 10', lambda v: v <= 10
    if name.startswith('A1'):
        need = 3 if 'Mountains' in name else 2 if 'Swamp' in name else 4
        return f'>= {need}', lambda v: v >= need
    if name.startswith('A2'):
        return '<= 20', lambda v: v <= 20
    if name.startswith('A3'):
        return '<= 10', lambda v: v <= 10
    if name.startswith(('B', 'D3', 'D4')):
        return '0', lambda v: v == 0
    if name.startswith('C1'):
        return '>= 70', lambda v: v >= 70
    if name.startswith('C2'):
        return '>= 60', lambda v: v >= 60
    if name.startswith('D1'):
        return '100', lambda v: v >= 100
    if name.startswith('D2'):
        return '>= 50', lambda v: v >= 50
    return '', lambda v: True


def worst(name, values):
    """The worst value across worlds: the lowest for checks that want a high value, the highest otherwise."""
    high_good = name.startswith(('A1', 'C', 'D1', 'D2'))
    return min(values) if high_good else max(values)


def main(dumps, out, commit=''):
    fixtures = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'fixtures.json')))['worlds']
    modes = [m for m in ('classic', 'young', 'smoothed') if os.path.exists(os.path.join(dumps, f"{fixtures[0]['name']}-{m}.txt"))]
    results = {mode: [measure(load(os.path.join(dumps, f"{w['name']}-{mode}.txt"))) for w in fixtures] for mode in modes}
    checks = list(results[modes[0]][0])
    lines = [f'# Terrain QA scorecard', '',
             f'{len(fixtures)} fixture worlds (tools/isoproof/terrain-qa/fixtures.json), modes: {", ".join(modes)}' +
             (f', commit {commit}' if commit else '') + '.', '',
             'Each cell: worst case across the worlds / median, then PASS or FAIL on the worst case.', '',
             '| Check | ' + ' | '.join(modes) + ' | Target |', '|' + '---|' * (len(modes) + 2)]
    fails = {mode: 0 for mode in modes}
    for name in checks:
        desc, ok = target(name)
        cells = []
        for mode in modes:
            values = [r[name] for r in results[mode] if name in r]
            if not values:
                cells.append('-')
                continue
            wv, med = worst(name, values), statistics.median(values)
            passed = ok(wv)
            fails[mode] += 0 if passed else 1
            cells.append(f'{wv:.0f} / {med:.0f} {"PASS" if passed else "FAIL"}')
        lines.append(f'| {name} | ' + ' | '.join(cells) + f' | {desc} |')
    if 'smoothed' in modes:
        lines += ['', '## E. Balance: smoothed against classic, same world', '', '| Check | worst / median | Target |', '|---|---|---|']
        bal = [balance(load(os.path.join(dumps, f"{w['name']}-classic.txt")), load(os.path.join(dumps, f"{w['name']}-smoothed.txt")),
                       load_features(os.path.join(dumps, f"{w['name']}-classic.txt")), load_features(os.path.join(dumps, f"{w['name']}-smoothed.txt")))
               for w in fixtures]
        for name in bal[0]:
            desc, ok = target(name)
            values = [b[name] for b in bal]
            wv = max(values)
            passed = ok(wv)
            fails['smoothed'] += 0 if passed else 1
            lines.append(f'| {name} | {wv:.1f} / {statistics.median(values):.1f} {"PASS" if passed else "FAIL"} | {desc} |')
    lines += ['', 'Failed checks: ' + ', '.join(f'{mode} {n} of {len(checks)}' for mode, n in fails.items()), '']
    open(out, 'w').write('\n'.join(lines) + '\n')
    print('\n'.join(lines[-2:]))


if __name__ == '__main__':
    main(*sys.argv[1:])
