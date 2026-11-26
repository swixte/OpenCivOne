import base64, json, os, sys, time, urllib.request, urllib.error
from concurrent.futures import ThreadPoolExecutor

OUT = '/tmp/mood-comp/raw'
KEY = os.environ['OPENAI_API_KEY']

LAYOUT = (
    "Composition (the image is the map, read it like an atlas): the sea fills the right third of the image and the lower right corner; "
    "the coastline runs from the top edge (about 65% across) down and leftward to the bottom edge (about 55% across), "
    "with a deep rounded bay biting into the land in the middle of the right half, and one small island out in the sea in the upper right. "
    "A snow-capped mountain range sits in the upper left, with a band of rolling hills below it on the left side. "
    "A river rises in the mountains and winds across the land to empty into the bay. "
    "Three or four large forest masses: one across the upper middle, one in the lower left, one along the river's middle course, a small wood near the coast. "
    "Open grassland and golden plains fill the gaps between, especially in the centre, beside the bay, and in the lower middle. "
    "Keep the outer edges of the image ordinary terrain (nothing important at the very edge). "
)
GROUND_RULES = (
    "Camera straight down, orthographic, like a satellite photo or a painted atlas map: no horizon, no sky, no perspective, "
    "trees seen from above as crowns, mountains seen from above as ridges with light on one side and shadow on the other. "
    "One continuous seamless painting with natural irregular boundaries between terrains: no tiles, no squares, no grid, no hexes. "
    "Absolutely no buildings, no towns, no people, no animals, no boats, no roads, no text, no labels, no seals, no signature, no border or frame. "
    "Dense and busy like a strategy-game world map region, every part of it varied terrain."
)

STYLES = {
    'ukiyo-e': dict(
        ground="A Japanese ukiyo-e woodblock print in the manner of Hokusai, showing a terrain map region from directly above. "
               "Black keyline outlines around every shape, flat layered colour with soft bokashi gradients, visible woodgrain texture, warm cream washi paper showing through. "
               "Prussian blue sea with Great Wave style curling crests and white claw-like foam along the coast, pale blue shallows; "
               "pine forests as dense clusters of dark green pine crowns with keyline; hills as rounded sage and ochre mounds with contour keylines; "
               "mountains as grey-blue ridges with white snow caps; river as a blue ribbon with fine keyline ripples; grassland soft green, plains warm ochre. ",
        look="Japanese ukiyo-e woodblock print in the manner of Hokusai: bold black keyline, flat colour, woodgrain texture, Prussian blue, cinnabar red, cream, pine green",
        castle="a Japanese castle compound: a white-walled multi-tier tenshu keep with cinnabar and dark tiled roofs, surrounded by grey stone walls with corner turrets and a few houses inside",
        town="a small Japanese village: a cluster of houses with cinnabar and thatched roofs, a small red pagoda, inside a low wooden palisade",
        rider="a mounted samurai on a horse, armoured in red lacquered armour, holding a spear and a small banner on his back, horse in mid-stride",
        foot="an ashigaru foot soldier with a tall yari spear and conical jingasa hat, standing ready",
        settler="a peasant settler leading an ox pulling a small two-wheeled cart loaded with bundles of rice straw",
        ship="a Japanese atakebune warship with a large square sail, wooden hull, a cabin with a red roof, and oars",
        portrait="A ukiyo-e woodblock bust portrait in the manner of Hokusai and Sharaku of a Japanese daimyo lord in black eboshi cap and patterned robes, stern face, bold black keyline, flat colour, woodgrain texture, cream paper background, centered head and shoulders, no text, no seals, no signature",
    ),
    'classicism': dict(
        ground="An old-master oil painting on canvas in the manner of Claude Lorrain and Nicolas Poussin: a painted map of a landscape region seen from directly above, as a 17th-century painter would paint an aerial view. "
               "Not a photograph, not a 3D render, not a satellite image: visible soft oil brushstrokes, translucent glazes, faint canvas weave, simplified painterly forms. "
               "Warm honey-gold daylight bathes everything. Bright fresh green meadows, golden wheat plains, deep green forests painted as masses of rounded oak and umbrella-pine crowns dabbed with the brush, "
               "hills as soft sunlit olive swells, pale grey-violet rocky mountains with a little snow, a silver river, a lapis lazuli sea with clear turquoise shallows and creamy surf along a sandy coast. ",
        look="classical oil painting in the manner of Claude Lorrain and Poussin: honey-gold daylight, pale limestone, terracotta roofs, lapis blue and crimson cloth, crisp clear forms",
        castle="a classical walled citadel: pale limestone walls with round towers, inside them a temple with columns and pediment, terracotta-roofed houses and a tall tower",
        town="a small classical town: a few pale stone houses with terracotta roofs and a small columned temple inside a low stone wall",
        rider="a classical cavalryman on a white horse, bronze helmet with crimson crest, crimson cloak, holding a lance, horse in mid-stride",
        foot="a classical hoplite soldier with a round bronze shield, long spear, crested bronze helmet and crimson cloak, standing ready",
        settler="a settler in a tunic leading an ox pulling a two-wheeled wooden cart loaded with sacks and tools",
        ship="an ancient Greek trireme warship with a square white sail with a crimson stripe, three banks of oars and a bronze ram",
        portrait="A classical oil portrait bust in the manner of Poussin and Jacques-Louis David of a Roman statesman with a laurel wreath and white toga with crimson border, honey-gold light, soft neutral background, head and shoulders centered, no text, no signature",
    ),
    'baroque': dict(
        ground="A Baroque oil painting in the manner of Velazquez, showing a terrain map region from directly above. "
               "Heavy visible impasto brushstrokes, strong chiaroscuro with deep shadows and glowing lights, rich and dark. "
               "Deep ultramarine sea, darker in the deep water, with creamy impasto surf along the coast; gold-green grassland, ochre plains; "
               "green-black forests of dense tree crowns; hills as dark olive swells with gold light on their crests; "
               "mountains as dark umber rock ridges with lead-white snow; a dark glinting river. ",
        look="Baroque oil painting in the manner of Velazquez: heavy impasto, strong chiaroscuro, gold light on dark shadow, crimson accents, ochre stone, dark slate roofs",
        castle="a Baroque fortified town: star-shaped bastion walls of ochre stone, inside them a domed cathedral with a bell tower, dark red and slate roofs, a crimson banner on the tower",
        town="a small Baroque town: a cluster of ochre and white houses with red tile roofs around a church with a bell tower, inside a low wall",
        rider="a Spanish conquistador knight on a dark horse, steel breastplate and morion helmet with a crimson plume, crimson sash, holding a lance, horse in mid-stride",
        foot="a Spanish musketeer with a matchlock musket on his shoulder, broad-brimmed hat with crimson plume, buff coat and crimson sash, standing ready",
        settler="a settler leading an ox pulling a two-wheeled wooden cart loaded with barrels and sacks",
        ship="a Spanish galleon with billowing cream sails bearing a crimson cross, a dark wooden hull with gilded stern",
        portrait="A Baroque oil portrait bust in the manner of Velazquez of a Spanish king with a dark moustache, black doublet, white lace golilla collar and gold chain, heavy impasto, dramatic chiaroscuro, dark background, head and shoulders centered, no text, no signature",
    ),
}

SPRITE_RULES = (
    "A single game sprite of {what}, seen from a high three-quarter top-down angle (camera above and in front, looking down at about 60 degrees). "
    "Painted as {look}. Bold clear silhouette, bright and crisp, strong contrast, readable at small size. "
    "The object is centered and fills most of the frame, fully visible, nothing cut off. "
    "Background: perfectly flat solid pure magenta #FF00FF everywhere around the object, no ground, no grass, no cast shadow, no gradient, no border, no text."
)


def jobs():
    for name, s in STYLES.items():
        yield f'{name}-ground', s['ground'] + LAYOUT + GROUND_RULES, '1536x1024', 'high'
        for k in ['castle', 'town', 'rider', 'foot', 'settler', 'ship']:
            yield f'{name}-{k}', SPRITE_RULES.format(what=s[k], look=s['look']), '1024x1024', 'medium'
        yield f'{name}-portrait', s['portrait'], '1024x1024', 'medium'


def gen(job):
    name, prompt, size, quality = job
    path = f'{OUT}/{name}.png'
    if os.path.exists(path):
        return name, 'exists'
    body = json.dumps(dict(model='gpt-image-2', prompt=prompt, size=size, quality=quality, n=1)).encode()
    for attempt in range(3):
        req = urllib.request.Request('https://api.openai.com/v1/images/generations', body,
                                     {'Authorization': f'Bearer {KEY}', 'Content-Type': 'application/json'})
        try:
            with urllib.request.urlopen(req, timeout=600) as r:
                data = json.load(r)
            open(path, 'wb').write(base64.b64decode(data['data'][0]['b64_json']))
            return name, 'ok'
        except urllib.error.HTTPError as e:
            msg = e.read().decode()[:300]
            if e.code in (401, 402, 403, 429) or 'billing' in msg:
                return name, f'FATAL {e.code} {msg}'
            err = f'{e.code} {msg}'
        except Exception as e:
            err = repr(e)
        time.sleep(5 * (attempt + 1))
    return name, f'failed {err}'


if __name__ == '__main__':
    sel = sys.argv[1:]
    js = [j for j in jobs() if not sel or any(j[0].startswith(p) for p in sel)]
    with ThreadPoolExecutor(8) as ex:
        for name, status in ex.map(gen, js):
            print(name, status, flush=True)
