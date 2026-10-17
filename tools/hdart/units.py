"""Unit art, 30x30 (15x15 in the original), in UnitTypeEnum order.

The game copies the unit row once per player and swaps color index 10 to the player color and index 2
to the player's second color. Primary key pixels ('P') stand for index 10 and secondary key pixels
('S') for index 2, so they show each player's colors.
"""

import numpy as np
from common import ramp, Sprite

M = dict(
    skin=ramp('#7a4a2a', '#a8704a', '#d39a6c', '#f0c294', '#fcdcb8'),
    hair=ramp('#1a120a', '#2e2014', '#44301e'),
    cloth=ramp('#3a2a18', '#5a4228', '#7a5a38', '#9a764a', '#ba945e'),
    leather=ramp('#2e1c0e', '#4a2e16', '#6a4420', '#8a5c2e'),
    dark=ramp('#0e0e12', '#1c1c22', '#2a2a32', '#3a3a44', '#4c4c58'),
    metal=ramp('#3a3e44', '#5a6068', '#7e868e', '#a6aeb6', '#d0d6dc'),
    bronze=ramp('#4a2e0e', '#6e4616', '#946220', '#ba8232', '#dcaa50'),
    gold=ramp('#6a4a08', '#9a700e', '#c89a1e', '#e8c040', '#fae27a'),
    wood=ramp('#3d2814', '#5a3c1e', '#7a5530', '#9c7244', '#bd925e'),
    horse=ramp('#3a2010', '#5a3418', '#7a4a22', '#9a6430', '#ba8044'),
    grey_horse=ramp('#4a4a4e', '#6a6a70', '#8a8a92', '#acacb4', '#cecfd6'),
    mane=ramp('#140c08', '#24160e', '#382418'),
    white=ramp('#9aa2aa', '#bcc4cc', '#dce2e8', '#f2f6f8', '#ffffff'),
    canvas=ramp('#8a826a', '#aaa286', '#c8c0a2', '#e2dcc0', '#f6f2dc'),
    olive=ramp('#2a3016', '#3e461e', '#545e28', '#6c7834', '#889444'),
    navy=ramp('#30363e', '#465060', '#5e6a7a', '#7a8696', '#9aa6b4'),
    hull=ramp('#1e140c', '#3a2614', '#56391e', '#74502a', '#946a3a'),
    red=ramp('#5a1010', '#8a1a18', '#b82a22', '#d84838', '#f07060'),
    glass=ramp('#1a3a5a', '#2a5a8a', '#4a86b6', '#7ab2da', '#b0d8f0'),
    camel=ramp('#6a4a22', '#8a642e', '#aa823e', '#c8a056', '#e0bc74'),
    fire=ramp('#7a1404', '#c03008', '#f06810', '#ffa830', '#ffe080'),
    water=ramp('#1a4a7a', '#2a66a0', '#4a86c0', '#7aaedc', '#b4d8f0'),
    P='P', S='S',
)

GROUND_SHADOW = (15, 27.5, 10.5, 2.4, 80)
AIR_SHADOW = (17, 28, 8, 1.8, 60)
SEA_SHADOW = None


def _s():
    return Sprite(30, 30, M)


# ---------- people ----------

def _person(s, x=13, body='P', legs='cloth', helmet=None, plume=None, hat=None, skin='skin', boots='dark'):
    s.rect(x - 2, 19, x, 26, legs, -0.2)
    s.rect(x + 1, 19, x + 3, 26, legs, -0.6)
    s.rect(x - 3, 25, x, 27, boots, 0)
    s.rect(x + 1, 25, x + 4, 27, boots, -0.3)
    s.rect(x - 3, 11, x + 4, 19, body, 0)
    s.rect(x - 3, 18, x + 4, 19.5, 'leather', 0)
    s.ellipse(x + 0.5, 8, 2.8, 3, skin, 0.3, shade=1)
    if helmet:
        s.ellipse(x + 0.5, 6.5, 3.2, 2.4, helmet, 0.4, shade=1.2)
        s.rect(x - 2.7, 6.5, x + 3.8, 8, helmet, -0.2)
    if plume:
        s.ellipse(x + 0.5, 3.3, 3.5, 1.4, plume, 0.3)
    if hat == 'tricorn':
        s.poly([(x - 4, 6.5), (x + 5, 6.5), (x + 3, 3.5), (x - 2, 3.5)], 'dark', 0.2)
    elif hat == 'brimmed':
        s.rect(x - 4, 6, x + 5, 7, 'canvas', -0.3)
        s.ellipse(x + 0.5, 5, 2.8, 2, 'canvas', 0.2)
    elif hat == 'kepi':
        s.rect(x - 2.5, 3.5, x + 3.5, 6.5, 'olive', 0.2)
        s.rect(x + 1, 6, x + 5, 7, 'dark', 0)
    elif hat == 'bowler':
        s.ellipse(x + 0.5, 5, 3, 2, 'dark', 0.3)
        s.rect(x - 3.5, 6, x + 4.5, 7, 'dark', 0)
    s.ellipse(x + 2, 8, 0.6, 0.7, 'hair', 0)


def _arm_to(s, x, hx, hy, body='P'):
    s.line(x + 3, 12.5, hx, hy, 2.6, body, -0.2)
    s.ellipse(hx, hy, 1.4, 1.4, 'skin', 0.3)


def settler():
    s = _s()
    s.ellipse(21, 17, 5, 5, 'canvas', 0, shade=1)          # bundle on the back
    s.line(18, 13, 25, 20, 1.2, 'leather', 0)
    _person(s, 12, hat='brimmed')
    s.line(4, 26, 9, 8, 1.6, 'wood', 0.2)                    # shovel
    s.poly([(2, 26), (6, 26), (5, 29), (3, 29)], 'metal', 0.3)
    _arm_to(s, 12, 7, 15)
    return s.render(shadow=GROUND_SHADOW)


def militia():
    s = _s()
    _person(s, 13, legs='cloth')
    s.line(20, 27, 21, 4, 1.6, 'wood', 0.2)                  # pitchfork
    for dx in (-1.5, 0, 1.5):
        s.line(21 + dx, 6, 21 + dx, 2, 0.9, 'metal', 0.4)
    s.rect(19.5, 5.5, 22.8, 6.5, 'metal', 0)
    _arm_to(s, 13, 20, 14)
    return s.render(shadow=GROUND_SHADOW)


def phalanx():
    s = _s()
    s.line(22, 28, 22, 1, 1.4, 'wood', 0.2)
    s.poly([(20.5, 3), (22, -0.5), (23.5, 3)], 'bronze', 0.6)
    _person(s, 13, legs='bronze', helmet='bronze', plume='S')
    _arm_to(s, 13, 21, 14)
    s.ellipse(9, 16, 6, 7, 'P', 0.2, shade=1.2)              # round hoplon shield
    s.ellipse(9, 16, 6, 7, 'P', 0.2, shade=1.2)
    s.ellipse(9, 16, 2.2, 2.4, 'S', 0.3)
    return s.render(shadow=GROUND_SHADOW)


def legion():
    s = _s()
    _person(s, 14, legs='skin', helmet='metal', plume='red')
    s.line(18, 15, 26, 8, 1.6, 'metal', 0.8)                 # gladius
    s.rect(17, 14, 19, 17, 'gold', 0)
    _arm_to(s, 14, 18, 15)
    s.rect(4, 9, 12, 25, 'P', 0.2)                           # scutum
    s.rect(7, 9, 9, 25, 'S', 0)
    s.ellipse(8, 17, 1.6, 1.6, 'gold', 0.4)
    return s.render(shadow=GROUND_SHADOW)


def musketeers():
    s = _s()
    _person(s, 13, legs='white', hat='tricorn')
    s.rect(10, 11, 17, 13, 'S', 0.2)                         # cross belts
    s.line(20, 27, 20, 2, 1.6, 'wood', 0)
    s.line(20, 10, 20, 1, 1.0, 'metal', 0.4)
    _arm_to(s, 13, 19, 15)
    return s.render(shadow=GROUND_SHADOW)


def riflemen():
    s = _s()
    _person(s, 12, legs='olive', hat='kepi')
    s.line(8, 22, 26, 9, 1.6, 'wood', -0.2)                  # rifle held across
    s.line(20, 13, 29, 6.5, 0.9, 'metal', 0.5)
    _arm_to(s, 12, 19, 15)
    s.rect(9, 13, 16, 15, 'S', 0)
    return s.render(shadow=GROUND_SHADOW)


def diplomat():
    s = _s()
    _person(s, 13, body='dark', legs='dark', hat='bowler')
    s.line(10, 11, 16, 18, 1.6, 'P', 0.4)                    # sash
    s.rect(17, 19, 24, 24, 'leather', 0.2)                   # briefcase
    s.rect(19, 18, 22, 19, 'leather', -0.4)
    _arm_to(s, 13, 18, 18, body='dark')
    return s.render(shadow=GROUND_SHADOW)


# ---------- mounted ----------

def _horse(s, hx=15, coat='horse', cloth=None):
    s.ellipse(hx, 18, 9, 4.6, coat, 0.1, shade=1.2)
    s.poly([(hx + 6, 16), (hx + 10, 8), (hx + 13, 9), (hx + 11, 14), (hx + 8, 19)], coat, 0.3)
    s.ellipse(hx + 12, 10, 2.8, 2.2, coat, 0.4)
    s.poly([(hx + 7, 15), (hx + 10, 8), (hx + 11, 9), (hx + 9, 15)], 'mane', 0)
    for lx, t in ((hx - 7, -0.3), (hx - 4, -0.8), (hx + 3, -0.3), (hx + 6, -0.8)):
        s.rect(lx, 21, lx + 2, 27, coat, t)
        s.rect(lx, 26, lx + 2, 27.5, 'dark', 0)
    s.line(hx - 9, 16, hx - 12, 23, 1.8, 'mane', 0)
    if cloth:
        s.rect(hx - 8, 15, hx + 6, 22, cloth, 0)
        s.rect(hx - 8, 21, hx + 6, 22.5, 'S', 0)


def cavalry():
    s = _s()
    _horse(s, 14)
    s.rect(9, 9, 15, 17, 'P', 0.2)                           # rider
    s.ellipse(12, 6, 2.6, 2.6, 'skin', 0.3)
    s.rect(9, 2, 15, 5, 'dark', 0.2)                         # shako
    s.ellipse(12, 2, 1.4, 1, 'S', 0)
    s.line(14, 11, 23, 3, 1.2, 'metal', 0.7)                 # sabre
    s.rect(10, 15, 13, 21, 'white', -0.3)
    return s.render(shadow=GROUND_SHADOW)


def knights():
    s = _s()
    _horse(s, 14, coat='grey_horse', cloth='P')
    s.rect(9, 7, 15, 16, 'metal', 0.2)
    s.ellipse(12, 5, 3, 3, 'metal', 0.6, shade=1)
    s.rect(10, 4.5, 15, 5.5, 'dark', 0)
    s.line(4, 16, 29, 4, 1.4, 'wood', 0.3)                   # lance
    s.poly([(9, 9), (5, 9), (5, 15), (7, 17), (9, 15)], 'S', 0.3)   # shield
    return s.render(shadow=GROUND_SHADOW)


def chariot():
    s = _s()
    _horse(s, 19)
    s.line(4, 18, 14, 18, 1.2, 'wood', 0)
    s.poly([(2, 12), (10, 12), (11, 22), (2, 22)], 'P', 0.3)
    s.rect(2, 12, 10, 14, 'S', 0.2)
    s.ellipse(6, 23, 4, 4, 'wood', 0, shade=1)
    s.ellipse(6, 23, 1.2, 1.2, 'bronze', 0.4)
    s.ellipse(6, 7, 2.4, 2.4, 'skin', 0.3)
    s.rect(4, 9, 8, 13, 'S', 0)
    return s.render(shadow=GROUND_SHADOW)


def caravan():
    s = _s()
    s.ellipse(14, 16, 8, 5, 'camel', 0.1, shade=1.2)
    s.ellipse(12, 11, 4, 3.4, 'camel', 0.4, shade=1)          # hump
    s.poly([(20, 15), (24, 7), (27, 7), (25, 12), (22, 18)], 'camel', 0.3)
    s.ellipse(26.5, 7.5, 2.4, 1.8, 'camel', 0.4)
    for lx, t in ((8, -0.3), (11, -0.8), (17, -0.3), (20, -0.8)):
        s.rect(lx, 19, lx + 2, 27, 'camel', t)
    s.rect(6, 12, 13, 18, 'P', 0.2)                           # packs
    s.rect(14, 12, 20, 18, 'S', 0.2)
    s.line(6, 15, 20, 15, 1, 'leather', 0)
    return s.render(shadow=GROUND_SHADOW)


# ---------- machines ----------

def _wheel(s, cx, cy, r, mat='wood'):
    s.ellipse(cx, cy, r, r, mat, 0, shade=1)
    s.ellipse(cx, cy, r * 0.35, r * 0.35, 'metal', 0.4)


def catapult():
    s = _s()
    s.rect(3, 18, 26, 21, 'wood', 0.2)
    s.poly([(8, 18), (14, 8), (16, 8), (12, 18)], 'wood', -0.2)
    s.line(10, 19, 26, 4, 1.8, 'wood', 0.4)                  # throwing arm
    s.ellipse(25, 4.5, 2.4, 2, 'leather', 0)
    s.ellipse(25, 3.8, 1.5, 1.4, 'dark', 0.3)
    _wheel(s, 7, 23, 3.6)
    _wheel(s, 22, 23, 3.6)
    s.line(4, 18, 4, 7, 1, 'wood', 0)
    s.rect(4.5, 7, 10, 11, 'P', 0.2)                          # pennant
    return s.render(shadow=GROUND_SHADOW)


def cannon():
    s = _s()
    s.poly([(3, 22), (15, 13), (19, 17), (8, 24)], 'P', 0.1)  # carriage
    s.line(9, 16, 28, 9, 3.6, 'bronze', 0.5)
    s.ellipse(8, 17, 2.4, 2.4, 'bronze', 0.2)
    _wheel(s, 15, 21, 5.5)
    return s.render(shadow=GROUND_SHADOW)


def artillery():
    s = _s()
    s.poly([(2, 24), (14, 17), (16, 20), (5, 26)], 'olive', 0)
    s.line(10, 17, 29, 6, 2.4, 'olive', 0.4)
    s.rect(9, 13, 18, 20, 'olive', 0.3)                       # gun shield
    s.rect(9, 13, 18, 15, 'P', 0.2)
    _wheel(s, 13, 23, 4.2, 'dark')
    return s.render(shadow=GROUND_SHADOW)


def armor():
    s = _s()
    s.poly([(2, 20), (27, 20), (25, 26), (4, 26)], 'dark', 0)
    for wx in (6, 10, 14, 18, 22):
        s.ellipse(wx, 23, 2, 2, 'metal', 0)
    s.poly([(3, 15), (27, 15), (28, 20), (2, 20)], 'olive', 0.2)
    s.poly([(9, 9), (20, 9), (22, 15), (8, 15)], 'olive', 0.4)
    s.line(20, 11.5, 30, 11.5, 1.8, 'olive', 0.2)
    s.rect(10, 11, 16, 13.5, 'P', 0.3)                        # turret marking
    s.rect(4, 16, 9, 18, 'S', 0)
    return s.render(shadow=GROUND_SHADOW)


def mech_infantry():
    s = _s()
    s.poly([(2, 21), (28, 21), (26, 26), (4, 26)], 'dark', 0)
    for wx in (6, 15, 24):
        s.ellipse(wx, 24, 2.8, 2.8, 'dark', 0.4, shade=1)
    s.poly([(3, 12), (22, 12), (28, 17), (28, 21), (2, 21)], 'olive', 0.2)
    s.rect(6, 14, 10, 17, 'glass', 0.3)
    s.rect(12, 14, 16, 17, 'glass', 0.3)
    s.rect(3, 18, 27, 20, 'P', 0)
    s.line(16, 11, 23, 8, 1.2, 'metal', 0)
    return s.render(shadow=GROUND_SHADOW)


# ---------- aircraft and missile (seen from above, casting a shadow below) ----------

def fighter():
    s = _s()
    s.poly([(15, 1), (17, 6), (17, 22), (19, 27), (11, 27), (13, 22), (13, 6)], 'navy', 0.3)
    s.poly([(13, 11), (2, 19), (2, 21), (13, 17)], 'navy', 0)
    s.poly([(17, 11), (28, 19), (28, 21), (17, 17)], 'navy', -0.3)
    s.ellipse(15, 8, 1.4, 2.6, 'glass', 0.6)
    s.ellipse(6, 19, 1.8, 1.8, 'P', 0.3)
    s.ellipse(24, 19, 1.8, 1.8, 'P', 0.3)
    s.rect(13.5, 22, 16.5, 25, 'S', 0)
    return s.render(shadow=AIR_SHADOW)


def bomber():
    s = _s()
    s.poly([(14, 1), (16, 1), (17, 5), (17, 25), (15, 28), (13, 25), (13, 5)], 'navy', 0.3)
    s.poly([(13, 9), (0, 14), (0, 17), (13, 15)], 'navy', 0)
    s.poly([(17, 9), (30, 14), (30, 17), (17, 15)], 'navy', -0.3)
    s.poly([(13, 23), (8, 26), (8, 28), (13, 27)], 'navy', 0)
    s.poly([(17, 23), (22, 26), (22, 28), (17, 27)], 'navy', -0.3)
    for ex in (4, 9, 21, 26):
        s.ellipse(ex, 13.5, 1.3, 2.2, 'dark', 0.2)
    s.ellipse(15, 4.5, 1.2, 2, 'glass', 0.6)
    s.rect(4, 15, 10, 16.5, 'P', 0.3)
    s.rect(20, 15, 26, 16.5, 'P', 0.3)
    s.rect(13.5, 18, 16.5, 21, 'S', 0)
    return s.render(shadow=AIR_SHADOW)


def nuclear():
    s = _s()
    s.poly([(15, 1), (18, 6), (18, 22), (12, 22), (12, 6)], 'white', 0.3)
    s.poly([(12, 18), (8, 25), (12, 23)], 'P', 0)
    s.poly([(18, 18), (22, 25), (18, 23)], 'P', -0.3)
    s.rect(12, 9, 18, 12, 'P', 0.3)
    s.ellipse(15, 15, 2, 2, 'gold', 0.4)
    s.ellipse(15, 25, 2.4, 2.6, 'fire', 0.6)
    s.ellipse(15, 27.5, 1.4, 1.4, 'fire', 1.4)
    return s.render(shadow=AIR_SHADOW)


# ---------- ships (side view, on the water line) ----------

WATER_LINE = 23


def _wake(s, x0=3, x1=27):
    s.rect(x0, WATER_LINE + 3, x1, WATER_LINE + 4, 'water', 0.8)
    s.rect(x0 - 2, WATER_LINE + 2, x0 + 2, WATER_LINE + 3, 'white', -0.5)


def _hull(s, x0, x1, top, depth, mat='hull', bow=4, stripe=None):
    s.poly([(x0, top), (x1, top - 1), (x1 - bow, WATER_LINE + depth), (x0 + 2, WATER_LINE + depth)], mat, 0.1)
    if stripe:
        s.poly([(x0 + 0.5, top + 1), (x1 - 0.5, top), (x1 - 1.2, top + 2), (x0 + 1, top + 3)], stripe, 0.2)


def trireme():
    s = _s()
    _wake(s)
    _hull(s, 2, 29, 18, 2, stripe='S')
    for ox in range(6, 26, 3):
        s.line(ox, 21, ox - 2, 27, 0.9, 'wood', -0.2)
    s.line(15, 18, 15, 3, 1.2, 'wood', 0)
    s.rect(9, 4, 21, 14, 'P', 0.2)
    s.rect(9, 7, 21, 9, 'S', 0)
    return s.render()


def sail():
    s = _s()
    _wake(s)
    _hull(s, 3, 28, 17, 2, stripe='P')
    for mx, top, w in ((11, 3, 5), (20, 5, 4.5)):
        s.line(mx, 17, mx, top, 1, 'wood', 0)
        s.poly([(mx - w, top + 2), (mx + w, top + 2), (mx + w + 1, 15), (mx - w - 1, 15)], 'canvas', 0.3)
        s.rect(mx - 0.5, top + 3, mx + 1.5, 15, 'P', 0.2)
        s.rect(mx - w, top + 7.5, mx + w, top + 9, 'P', 0.2)
    return s.render()


def frigate():
    s = _s()
    _wake(s)
    _hull(s, 1, 29, 16, 3, mat='dark', stripe='P')
    for gx in range(5, 26, 4):
        s.rect(gx, 18.5, gx + 1.5, 19.8, 'gold', 0)
    for mx, top, w in ((7, 3, 3.6), (15, 1, 4.4), (23, 4, 3.4)):
        s.line(mx, 16, mx, top, 1, 'wood', 0)
        s.poly([(mx - w, top + 2), (mx + w, top + 2), (mx + w, 9), (mx - w, 9)], 'canvas', 0.4)
        s.poly([(mx - w, 10), (mx + w, 10), (mx + w + 0.5, 15), (mx - w - 0.5, 15)], 'canvas', 0)
    s.rect(14, 0, 16, 2, 'S', 0)
    return s.render()


def ironclad():
    s = _s()
    _wake(s)
    _hull(s, 2, 28, 19, 2, mat='navy')
    s.poly([(7, 19), (9, 13), (21, 13), (23, 19)], 'dark', 0.2)
    s.rect(13, 5, 16, 13, 'dark', 0.3)
    s.rect(13, 7, 16, 9, 'P', 0.3)
    s.ellipse(14.5, 3.5, 2.5, 1.8, 'navy', 0.6)
    s.rect(10, 15, 12, 16.5, 'gold', 0)
    s.rect(18, 15, 20, 16.5, 'gold', 0)
    return s.render()


def cruiser():
    s = _s()
    _wake(s)
    _hull(s, 1, 29, 18, 2, mat='navy', bow=5)
    s.rect(9, 12, 20, 18, 'navy', 0.4)
    s.rect(11, 7, 17, 12, 'navy', 0.6)
    s.rect(13, 3, 15, 7, 'navy', 0.2)
    s.rect(16, 8, 18, 12, 'P', 0.3)                           # funnel
    s.rect(4, 15, 8, 18, 'navy', 0.2)
    s.line(5, 15.5, 1, 14.5, 1, 'dark', 0)
    s.rect(21, 15, 25, 18, 'navy', 0.2)
    s.line(24, 15.5, 29, 14.5, 1, 'dark', 0)
    s.rect(9, 17, 20, 18, 'S', 0)
    return s.render()


def battleship():
    s = _s()
    _wake(s, 1, 29)
    _hull(s, 0, 30, 17, 3, mat='navy', bow=5)
    s.rect(8, 10, 21, 17, 'navy', 0.4)
    s.rect(11, 4, 17, 10, 'navy', 0.6)
    s.rect(17, 6, 20, 10, 'P', 0.3)
    for tx, d in ((3, -1), (24, 1)):
        s.rect(tx, 13, tx + 4, 17, 'navy', 0.2)
        s.line(tx + 2, 14, tx + 2 + d * 5, 13, 1.2, 'dark', 0)
    s.rect(8, 16, 21, 17, 'S', 0)
    return s.render()


def submarine():
    s = _s()
    _wake(s, 6, 26)
    s.ellipse(15, 22, 13, 3.4, 'dark', 0.2, shade=1)
    s.poly([(12, 19), (13, 12), (19, 12), (20, 19)], 'dark', 0.4)
    s.line(16, 12, 16, 7, 1, 'metal', 0)
    s.rect(13, 14, 19, 16, 'P', 0.3)
    s.rect(4, 21, 8, 22.5, 'S', 0)
    return s.render()


def carrier():
    s = _s()
    _wake(s, 0, 30)
    _hull(s, 0, 30, 18, 3, mat='navy', bow=3)
    s.rect(0, 15, 30, 18, 'navy', 0.5)                        # flight deck
    s.rect(2, 16, 28, 16.6, 'white', 0.2)
    s.rect(20, 7, 25, 15, 'navy', 0.3)
    s.rect(21, 4, 23, 7, 'metal', 0)
    s.rect(20, 10, 25, 12, 'P', 0.3)
    s.poly([(6, 13), (9, 12), (12, 13), (9, 14)], 'S', 0.3)
    return s.render()


def transport():
    s = _s()
    _wake(s)
    _hull(s, 1, 29, 18, 3, mat='hull', bow=4, stripe='S')
    for i, bx in enumerate((4, 9, 14)):
        s.rect(bx, 12 - (i % 2) * 2, bx + 4.5, 18, 'P' if i % 2 == 0 else 'S', 0.3 - i * 0.2)
    s.rect(21, 8, 27, 18, 'white', 0.2)
    s.rect(22, 10, 26, 12, 'glass', 0.3)
    s.rect(23, 4, 25, 8, 'dark', 0)
    return s.render()


UNITS = [settler, militia, phalanx, legion, musketeers, riflemen, cavalry, knights, catapult, cannon,
         chariot, armor, mech_infantry, artillery, fighter, bomber, trireme, sail, frigate, ironclad,
         cruiser, battleship, submarine, carrier, transport, nuclear, diplomat, caravan]
