"""Picks the four fixed close-ups of a world for the terrain QA image pack: a polar edge, a temperate starting area, a
desert/jungle border and a river from source to mouth. Prints "x,y;x,y;x,y;x,y" for ISO_WORLD_VIEWS.

Usage: python3 closeups.py WORLD_DUMP
"""

import sys

import scorecard as sc


def main(path):
    g = sc.load(path)
    land = lambda t: t not in (sc.WATER, sc.ARCTIC)
    # the northernmost stretch of land below the ice
    polar = min(((y, x) for y in range(2, sc.H // 2) for x in range(sc.W) if land(g[y][x])))
    polar = (polar[1], polar[0] + 1)
    # the best start in the temperate belt
    temperate = [(s, x, y) for s, x, y in sc.starts(g, 20) if 0.3 <= sc.latitude(y) <= 0.65]
    temperate = (temperate[0][1], temperate[0][2]) if temperate else (40, 15)
    # where desert comes closest to jungle
    deserts = [(x, y) for y in range(sc.H) for x in range(sc.W) if g[y][x] == sc.DESERT]
    jungles = [(x, y) for y in range(sc.H) for x in range(sc.W) if g[y][x] == sc.JUNGLE]
    dist = lambda a, b: min(abs(a[0] - b[0]), sc.W - abs(a[0] - b[0])) + abs(a[1] - b[1])
    border = min(((dist(d, j), d, j) for d in deserts for j in jungles), default=(0, (40, 25), (40, 25)))
    border = (border[1][0], border[1][1])
    # the middle of the longest river
    systems = [c for t, c in sc.regions(g, lambda a, b: a == b == sc.RIVER) if t == sc.RIVER]
    river = max(systems, key=len, default=[(40, 25)])
    river = sorted(river, key=lambda c: (c[1], c[0]))[len(river) // 2]
    print(';'.join(f'{x},{y}' for x, y in (polar, temperate, border, river)))


if __name__ == '__main__':
    main(sys.argv[1])
