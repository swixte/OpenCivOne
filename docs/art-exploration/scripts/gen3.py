import sys
from concurrent.futures import ThreadPoolExecutor
import gen
from gen2 import A_STYLE, ANGLE

KEY = ("The {part} is the ONLY green thing in the picture: flat pure bright green #00C000, with a simple cream mon crest (a round family crest) printed on it. "
       "Nothing else in the picture is green. ")

SUBJECTS = {
    'rider': ("a mounted samurai on a horse in mid-stride, holding upright a very tall lance that rises high above him, "
              "with a long narrow vertical nobori banner hanging from the lance; the lance and banner make the whole figure tall", 'nobori banner'),
    'foot': ("an ashigaru foot soldier standing upright facing forward, holding a naginata (a long pole with a curved blade) upright beside him, "
             "a tall rectangular wooden shield standing in front of his legs, and a small square sashimono banner on a pole on his back", 'sashimono banner on his back'),
    'settler': ("a low, wide two-wheeled ox cart seen from the front-side, loaded with low straw bales and tools, pulled by an ox, "
                "a farmer in a straw hat walking at the ox's head; the whole group is wide and low, much wider than tall, "
                "with a small square cloth flag on a short pole stuck into the load", 'small square cloth flag on the cart'),
    'ship': ("a Japanese sailing ship with one very big square cream sail that dominates the picture and a dark wooden hull below; "
             "in the middle of the sail a large round disc", 'round disc in the middle of the sail'),
}
OBJECTS = {
    'walls': "a square ring of grey stone castle walls with white plastered tops, dark tile coping and a small corner turret at each of the four corners. The inside of the ring is completely empty and shows the flat magenta background",
    'fort': "a low ring-shaped earthwork fortification: a circular earthen bank topped with a fence of sharpened wooden stakes. The inside of the ring is completely empty and shows the flat magenta background. Low and flat, much wider than tall",
    'hut': "a small cluster of two thatched peasant huts and a straw-roofed storehouse",
    'mine': "a mine entrance: a dark tunnel mouth in a small rocky outcrop, framed by timber posts, with a small pile of ore beside it",
    'horses': "two wild horses grazing side by side, one brown and one cream",
    'fish': "two fish leaping out of a small curling wave, in the manner of Hokusai",
    'burst': "a stylized combat explosion: a compact round burst of curling cream smoke clouds with black keyline swirls, cinnabar and ochre flames bursting outward between them, a few black ink splatters around it",
    'smoke': "a tall narrow plume of dark grey smoke rising in stylized curls with black keyline, with a few small cinnabar flames at its base",
}


def jobs():
    for k, (subj, part) in SUBJECTS.items():
        yield f'ukiyo-e-M-{k}', A_STYLE + "The subject: " + subj + ". " + KEY.format(part=part) + ANGLE, '1024x1024', 'medium'
    for k, subj in OBJECTS.items():
        yield f'ukiyo-e-M-{k}', A_STYLE + "The subject: " + subj + ". " + ANGLE, '1024x1024', 'medium'


if __name__ == '__main__':
    sel = sys.argv[1:]
    js = [j for j in jobs() if not sel or any(j[0].startswith(p) for p in sel)]
    with ThreadPoolExecutor(12) as ex:
        for name, status in ex.map(gen.gen, js):
            print(name, status, flush=True)
