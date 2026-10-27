import sys
from concurrent.futures import ThreadPoolExecutor
import gen

ANGLE = ("Seen from a high three-quarter angle (camera above and in front, looking down at about 60 degrees). "
         "Bold simple silhouette, readable at small size. Centered, fills most of the frame, fully visible, nothing cut off. "
         "Background: perfectly flat solid pure magenta #FF00FF everywhere around it, no ground, no grass, no cast shadow, no gradient, no border, no text, no seal, no signature.")

A_STYLE = ("A Japanese ukiyo-e woodblock print figure in the manner of Hokusai: completely flat colour areas with no shading, no gradients, no highlights, no glossy rendering, "
           "strong even black keyline, limited palette of indigo, cinnabar red, ochre and cream only, slight visible woodgrain in the flat colour, "
           "slight print registration offset between the colour and the keyline. ")
A = {
    'rider': "a mounted samurai on a horse in mid-stride, holding a spear, a small banner on his back",
    'foot': "an ashigaru foot soldier with a tall yari spear and conical jingasa hat, standing ready",
    'settler': "a peasant settler leading an ox pulling a small two-wheeled cart loaded with bundles of rice straw",
    'ship': "a Japanese atakebune ship with one large square sail and a wooden hull with a small cabin",
    'castle': "a Japanese castle compound: a white multi-tier keep with dark tiled roofs, grey stone walls with corner turrets, a few houses inside",
    'town': "a small Japanese village: a cluster of houses with thatched and cinnabar roofs and a small pagoda inside a low wooden palisade",
}

B_FIG = {
    'rider': "a mounted samurai on a horse with a spear",
    'foot': "an ashigaru foot soldier with a tall yari spear and conical hat",
    'settler': "a peasant leading an ox pulling a small two-wheeled cart of rice straw",
    'ship': "a small Japanese ship with one square sail",
}
B_FACE = ("A Japanese lacquer game piece: a thick round lacquered wooden token disc, seen from slightly above and in front so its face is nearly round and its thick black rim with white mon crests shows below, "
          "the face lacquered solid {colour} with a thin gold ring; printed on the disc's face and filling it, like a mon crest or a seal design, {fig}, "
          "full figure from hat to feet, in ukiyo-e woodblock style with black keyline and flat indigo, cinnabar, ochre and cream colour. "
          "Bold, readable at small size. Centered, fills most of the frame. "
          "Background: perfectly flat solid pure magenta #FF00FF everywhere around it, no ground, no cast shadow, no gradient, no border, no text.")
B_TMPL = ("A Japanese lacquer game piece: a round lacquered wooden token disc, seen from a high three-quarter angle so the disc is a flattened ellipse with visible thickness, "
          "lacquered solid {colour}, with a thin gold rim and a black edge, a simple white mon crest pattern on its rim; "
          "standing on top of the disc, {fig}, printed in ukiyo-e woodblock style with black keyline and flat indigo, cinnabar, ochre and cream colour. "
          "The figure stands within the disc and is fully visible. Bold, readable at small size. Centered, fills most of the frame. "
          "Background: perfectly flat solid pure magenta #FF00FF everywhere around it, no ground, no cast shadow, no gradient, no border, no text.")

C_FIG = {
    'rider': ("a mounted samurai on a galloping horse holding a spear", "a tall sashimono banner on his back"),
    'foot': ("an ashigaru foot soldier with a tall yari spear and conical hat", "a sash and a small sashimono banner on his back"),
    'settler': ("a peasant leading an ox pulling a two-wheeled cart loaded with bundles", "the cloth tied over the cart's load"),
    'ship': ("a Japanese ship with a wooden hull and oars", "its single large square sail"),
}
C_TMPL = ("A bold sumi-ink silhouette of {fig}: a solid black ink shape, like a Japanese kage-e shadow picture or a black woodblock, "
          "with only a few crisp thin cream-white lines inside to separate the parts, and exactly one accent colour: {accent} in flat {colour}, "
          "the only colour in the image apart from black. Very graphic, very readable at small size, strong clear outline. ")
C_CITY = {
    'castle': "a Japanese castle compound with a multi-tier keep, stone walls and corner turrets",
    'town': "a small Japanese village of a few houses and a small pagoda inside a palisade",
}
C_CITY_TMPL = ("A bold sumi-ink silhouette of {fig}: solid black ink shapes with crisp cream-white lines and window openings to separate roofs and walls, "
               "like a Japanese kage-e shadow picture or a black woodblock, and the roof ridges picked out in flat cinnabar red. Very graphic, readable at small size. ")


def jobs():
    for k, v in A.items():
        yield f'ukiyo-e-A-{k}', A_STYLE + "The subject: " + v + ". " + ANGLE, '1024x1024', 'medium'
    for k, v in B_FIG.items():
        yield f'ukiyo-e-B-{k}', B_TMPL.format(colour='bright indigo blue', fig=v), '1024x1024', 'medium'
    for k in ['foot']:
        pass
    yield 'ukiyo-e-B-foot-rival', B_TMPL.format(colour='bright cinnabar red', fig=B_FIG['foot']), '1024x1024', 'medium'
    for k, (fig, acc) in C_FIG.items():
        yield f'ukiyo-e-C-{k}', C_TMPL.format(fig=fig, accent=acc, colour='bright blue') + ANGLE, '1024x1024', 'medium'
    yield 'ukiyo-e-C-foot-rival', C_TMPL.format(fig=C_FIG['foot'][0], accent=C_FIG['foot'][1], colour='bright cinnabar red') + ANGLE, '1024x1024', 'medium'
    for k, v in C_CITY.items():
        yield f'ukiyo-e-C-{k}', C_CITY_TMPL.format(fig=v) + ANGLE, '1024x1024', 'medium'


def face_jobs():
    yield 'ukiyo-e-B-foot', B_FACE.format(colour='bright indigo blue', fig=B_FIG['foot']), '1024x1024', 'medium'
    yield 'ukiyo-e-B-foot-rival', B_FACE.format(colour='bright cinnabar red', fig=B_FIG['foot']), '1024x1024', 'medium'


if __name__ == '__main__':
    sel = sys.argv[1:]
    js = [j for j in (face_jobs() if sel == ['face'] else jobs()) if not sel or sel == ['face'] or any(j[0].startswith(p) for p in sel)]
    with ThreadPoolExecutor(9) as ex:
        for name, status in ex.map(gen.gen, js):
            print(name, status, flush=True)
