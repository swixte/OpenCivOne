"""Puts one of each test unit next to the human player's settlers in a fresh CIVILn.SVE, and moves player 2's
settlers and a few of its units next door, so the painted units can be screenshotted in the real game.

Usage: python3 place_units.py IN.SVE OUT.SVE

Unit records start at byte 9920 (8 players x 128 units x 12 bytes: status, x, y, type, moves, special moves,
goto x, goto y, goto dir, visible-by mask, next unit, home city). Positions below suit the map these shots came
from; change them for another map.

The map's unit-presence layer (CIVILn.MAP, pixel x + 160) is not updated, so a placed unit only appears once it
has moved: move each human unit one step on the first turn, and the AI moves its own.
"""
import sys

UNITS = 9920


def put(d, player, slot, x, y, unit_type, moves=3):
    o = UNITS + (player * 128 + slot) * 12
    d[o:o + 12] = bytes([0, x, y, unit_type, moves, 0, 255, 255, 255, 0xff, 255, 255])


d = bytearray(open(sys.argv[1], 'rb').read())
# human (player 1): militia, phalanx, legion, knights, catapult, chariot, musketeers*, cannon*, settlers,
# trireme, sail (* old 4x art, for comparison)
human = [(1, 28, 20), (2, 29, 20), (3, 30, 20), (7, 31, 20), (8, 32, 20), (10, 29, 21), (4, 31, 21),
         (9, 32, 21), (0, 28, 21), (16, 30, 22), (17, 31, 22)]
for slot, (t, x, y) in enumerate(human, 1):
    put(d, 1, slot, x, y, t)
# player 2: settlers, legion, knights, phalanx, militia, trireme
put(d, 2, 0, 35, 20, 0)
for slot, (t, x, y) in enumerate([(3, 34, 20), (7, 34, 21), (2, 35, 21), (1, 36, 20), (16, 33, 22)], 1):
    put(d, 2, slot, x, y, t)
open(sys.argv[2], 'wb').write(d)
