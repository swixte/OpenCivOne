"""What each of the game's pictures shows, for generating a theme (see theme.py and docs/art-inventory.md).

BACKDROPS are shown whole: one generated image each, with any PLACED objects put into the rectangles the game copies out
of them. SHEETS are cut into pieces at fixed rectangles: each piece is generated alone and written as a region file.
A theme can change any backdrop subject in its theme.json.

Not listed yet: the palace, the city view roads, the spaceship and the animations; they keep their stand-in art.
Leaders are painted as one still bust each: their face parts, which change expressions, are cleared. MAP is the Earth's terrain stored as pixels: never paint it.
"""

# Text the game writes over a backdrop needs calm areas; most backdrops carry a title at the top.
CALM_TOP = 'Keep the top fifth of the picture dark and calm, without detail, for a title.'
CALM_ALL = 'Keep large calm, dark areas, as text and panels are drawn over the picture.'
# menus written straight onto the picture, without a window, use dark text: they need a light, plain area behind them
CALM_RIGHT = ('The right half of the picture is a plain, softly lit, light area (pale parchment or pale sky tones) '
              'without any detail, where dark text is written; the subject stays in the left half.')
CALM_RIGHT_THIRD = ('The right third of the picture is a plain, softly lit, light area (pale parchment or pale sky tones) '
                    'without any detail, where dark text is written; the subject stays in the left two thirds.')

BACKDROPS = {
    'BIRTH0': ('The young Earth seen from space, a molten world glowing under a dark sky.', CALM_TOP),
    'BIRTH1': ('Volcanoes and rivers of lava under ash clouds, the land forming.', CALM_TOP),
    'BIRTH2': ('Steaming primordial oceans under storm clouds and lightning.', CALM_TOP),
    'BIRTH3': ('The first life in a shallow sea, strange simple creatures in murky water.', CALM_TOP),
    'BIRTH4': ('Lush prehistoric land with giant ferns, mist and a winding river.', CALM_TOP),
    'BIRTH5': ('Great beasts roaming a primeval plain at dusk.', CALM_TOP),
    'BIRTH6': ('An ice age: glaciers and snowfields under a pale sun.', CALM_TOP),
    'BIRTH7': ('Early humans gathered around a fire at the mouth of a cave at night.', CALM_TOP),
    'BIRTH8': ('The first small village of huts beside a river, smoke rising, at dawn.', CALM_TOP),
    'BACK0A': ('An ancient throne room with stone columns and torches, seen from the foot of the throne.', CALM_ALL),
    'BACK1A': ('A classical palace hall with marble columns and banners.', CALM_ALL),
    'BACK2A': ('A medieval great hall with tapestries, a long table and tall windows.', CALM_ALL),
    'BACK3A': ('A renaissance palace chamber with paintings and a gilded ceiling.', CALM_ALL),
    'BACK0M': ('A grand 19th-century government hall with chandeliers.', CALM_ALL),
    'BACK1M': ('A 20th-century statesman\'s office with a large desk and flags.', CALM_ALL),
    'BACK2M': ('A modern government chamber with a curved desk and screens.', CALM_ALL),
    # the city view builds the city on this landscape, and settlers walk across its lower part when a city is founded
    'HILL': ('A wide, gently rising grassy landscape under an open sky; the lower two thirds are open, even ground '
             'with nothing on it, where a city will be built.', CALM_TOP),
    # world customisation menus are at x 200 and up, the difficulty and competition menus at x 160 and up
    'CUSTOM': ('A great globe on a stand in a scholar\'s study, maps and instruments around it.', CALM_RIGHT_THIRD),
    'DIFFS': ('A dark velvet cloth draped over a table in the left half, with nothing on it.', CALM_RIGHT),
    'ARCH': ('The ruins of a great civilization overgrown by forest, a broken arch in the foreground.', CALM_TOP),
    'DISCOVR1': ('Scholars in a candlelit library studying scrolls and instruments.', CALM_ALL),
    'DISCOVR2': ('An inventor\'s workshop full of models, gears and drawings.', CALM_ALL),
    'SAD': ('A gloomy, empty throne room in the rain.', CALM_ALL),
}

# Objects placed into fixed rectangles over a backdrop, (x, y, width, height): the game copies these rectangles out.
PLACED = {
    # one crown per difficulty level, shown again beside the next menus; clicking a crown also picks its level
    'DIFFS': [
        ((21, 6, 53, 47), 'a plain iron circlet'),
        ((80, 41, 53, 47), 'a simple bronze crown'),
        ((21, 76, 53, 47), 'a silver crown with a few points'),
        ((80, 111, 53, 47), 'a gold crown with jewels'),
        ((21, 146, 53, 47), 'a magnificent imperial crown, gold with many large jewels'),
    ],
}

# city population figures: the modern era on the top row, the ancient era below
POP = [
    'a happy modern townsman, cheering', 'a happy modern townswoman, waving',
    'a content modern townsman, standing calmly', 'a content modern townswoman, standing calmly',
    'an unhappy modern townsman, arms crossed', 'an unhappy modern townswoman, frowning',
    'a modern entertainer with a guitar', 'a modern tax collector with a ledger', 'a modern scientist in a lab coat',
    'a happy ancient townsman in a tunic, cheering', 'a happy ancient townswoman in a robe, waving',
    'a content ancient townsman in a tunic', 'a content ancient townswoman in a robe',
    'an unhappy ancient townsman in a tunic, arms crossed', 'an unhappy ancient townswoman in a robe, frowning',
    'an ancient entertainer playing a lyre', 'an ancient tax collector with a scroll', 'an ancient scholar with a scroll',
]

NEVER_PAINT = {'MAP'}


# Sheets cut into pieces at fixed rectangles. Each piece is written as a region file (hd/NAME/X_Y.png), so anything a
# theme does not paint keeps its stand-in art. Kinds:
#   scene   an opaque illustration filling its rectangle (Encyclopedia and discovery pictures)
#   object  a cut-out object resting on the bottom of its rectangle (city view buildings, houses, wonders)
#   figure  a cut-out person; a piece can also list bust rectangles that get the head and shoulders of the same figure
#   bust    a cut-out head and upper body (the leaders)
# A piece is (rect, subject) or (rect, subject, [bust_rect, ...]), rectangles as (x, y, width, height) in original
# pixels. 'clear' lists rectangles written as transparent regions, so the game draws nothing there.

ADVANCES = [
    'Alphabet', 'Code of Laws', 'Currency', 'Atomic Theory', 'Democracy', 'Monarchy', 'Astronomy', 'Map Making',
    'Navigation', 'Mathematics', 'Medicine', 'Physics', 'Engineering', 'University', 'Magnetism', 'Electronics',
    'Masonry', 'Bronze Working', 'Iron Working', 'Bridge Building', 'Invention', 'Computers', 'Writing', 'Steam Engine',
    'Trade', 'Ceremonial Burial', 'Mysticism', 'Nuclear Fission', 'Philosophy', 'Religion', 'Literacy',
    'Horseback Riding', 'Feudalism', 'The Wheel', 'Gunpowder', 'Industrialization', 'Chemistry', 'Combustion', 'Flight',
    'Advanced Flight', 'Space Flight', 'Mass Production', 'Pottery', 'Communism', 'The Republic', 'Construction',
    'Rocketry', 'The Corporation', 'Metallurgy', 'Railroad', 'Nuclear Power', 'Theory of Gravity', 'Steel', 'Banking',
    'Electricity', 'Refining', 'Explosives', 'Superconductor', 'Automobile', 'Genetic Engineering', 'Plastics',
    'Recycling', 'Chivalry', 'Robotics', 'Conscription', 'Labor Union', 'Fusion Power',
]

ADVANCE_SCENES = {
    'Alphabet': 'a scribe carving the first letters into a clay tablet',
    'Code of Laws': 'a lawgiver standing before a great stone stele carved with laws',
    'Currency': 'a merchant weighing gold and silver coins on a scale',
    'Atomic Theory': 'a 19th-century scientist with a model of an atom on his desk',
    'Democracy': 'citizens casting their votes into a ballot box in a hall',
    'Monarchy': 'a king on his throne receiving the homage of his nobles',
    'Astronomy': 'an astronomer on a tower studying the stars with an astrolabe',
    'Map Making': 'a cartographer drawing a map of coasts on parchment',
    'Navigation': 'a navigator with a sextant on the deck of a sailing ship',
    'Mathematics': 'a scholar drawing geometric figures with a compass and an abacus',
    'Medicine': 'a physician tending a patient among jars of herbs',
    'Physics': 'a scientist with a pendulum, prisms and weights',
    'Engineering': 'engineers raising a great stone arch with a wooden crane',
    'University': 'students listening to a master in a vaulted lecture hall',
    'Magnetism': 'a lodestone and a compass needle on a navigator\'s chart',
    'Electronics': 'a radio set full of glowing vacuum tubes',
    'Masonry': 'masons cutting and laying great stone blocks',
    'Bronze Working': 'a smith pouring molten bronze into a mould',
    'Iron Working': 'a blacksmith hammering glowing iron on an anvil',
    'Bridge Building': 'a stone arch bridge across a river',
    'Invention': 'an inventor in a workshop with gears and drawings of machines',
    'Computers': 'a room of large mainframe computers with tape reels and lights',
    'Writing': 'a scribe writing on a papyrus scroll',
    'Steam Engine': 'a steam engine with a great flywheel and pistons',
    'Trade': 'a busy market harbour with merchants trading goods from ships',
    'Ceremonial Burial': 'a burial mound with offerings and a mourning procession',
    'Mysticism': 'a mystic reading the future in the smoke of a brazier',
    'Nuclear Fission': 'scientists in a laboratory with a glowing reactor experiment',
    'Philosophy': 'philosophers in robes debating among marble columns',
    'Religion': 'a priest before an altar in a candlelit temple',
    'Literacy': 'a great library with shelves of scrolls and readers',
    'Horseback Riding': 'a rider taming a wild horse on the steppe',
    'Feudalism': 'a knight kneeling to swear fealty to his lord',
    'The Wheel': 'a wooden cart with solid wooden wheels pulled by oxen',
    'Gunpowder': 'an alchemist mixing black powder, a small explosion of sparks',
    'Industrialization': 'smoking factory chimneys over a brick industrial town',
    'Chemistry': 'a chemist with glass flasks and bubbling retorts',
    'Combustion': 'an early internal combustion engine on a workbench',
    'Flight': 'an early biplane taking off from a field',
    'Advanced Flight': 'a jet aircraft streaking across the sky',
    'Space Flight': 'a rocket lifting off from its launch pad in a column of fire',
    'Mass Production': 'an assembly line of identical automobiles in a factory',
    'Pottery': 'a potter shaping a clay pot on a wheel',
    'Communism': 'workers marching with red banners in front of a factory',
    'The Republic': 'senators in togas debating in a senate hall',
    'Construction': 'builders raising a large building with scaffolding and cranes',
    'Rocketry': 'a tall rocket standing in a gantry',
    'The Corporation': 'a glass skyscraper headquarters among office towers',
    'Metallurgy': 'molten metal poured from a crucible in a foundry',
    'Railroad': 'a steam locomotive pulling a train across the countryside',
    'Nuclear Power': 'the cooling towers of a nuclear power station',
    'Theory of Gravity': 'a scholar under an apple tree watching an apple fall, the moon in the sky',
    'Steel': 'a steel mill with glowing furnaces and girders',
    'Banking': 'bankers counting money behind the counter of a grand bank',
    'Electricity': 'lightning striking a rod and sparks between metal spheres',
    'Refining': 'an oil refinery with towers and pipes',
    'Explosives': 'miners blasting rock with dynamite in a quarry',
    'Superconductor': 'a glowing magnet levitating over a frosted superconductor',
    'Automobile': 'an early automobile driving down a country road',
    'Genetic Engineering': 'a scientist studying a glowing double helix of DNA',
    'Plastics': 'moulded plastic goods coming out of a modern machine',
    'Recycling': 'workers sorting materials at a recycling plant',
    'Chivalry': 'two armoured knights jousting at a tournament',
    'Robotics': 'robot arms building machines on a factory line',
    'Conscription': 'a column of young soldiers marching off to war',
    'Labor Union': 'workers on strike with placards at the factory gate',
    'Fusion Power': 'a ring-shaped fusion reactor glowing with plasma',
}

# the Encyclopedia picture for each advance: cell i of the ICONPG sheets shows advance ADVANCE_CELLS[i]
ADVANCE_CELLS = [24, 32, 11, 65, 26, 36, 13, 59, 53, 44, 39, 5, 1, 51, 4, 35, 16, 50, 52, 38, 29, 49, 22, 0, 21, 10, 33, 60,
                 19, 37, 45, 12, 15, 43, 30, 40, 2, 41, 17, 27, 18, 3, 61, 56, 57, 6, 34, 55, 14, 62, 20, 58, 23, 8, 64, 66,
                 63, 31, 9, 48, 42, 7, 47, 54, 28, 25, 46]

UNITS = [
    ('Settlers', 'settlers driving an ox cart of tools and supplies to a new land'),
    ('Militia', 'a band of peasant militia with spears and pitchforks'),
    ('Phalanx', 'a Greek phalanx of hoplites with round shields and long spears in close order'),
    ('Legion', 'a Roman legion marching with rectangular shields and standards'),
    ('Musketeers', 'a line of 17th-century musketeers firing a volley'),
    ('Riflemen', 'a line of 19th-century riflemen in greatcoats advancing'),
    ('Cavalry', 'a charge of 19th-century cavalry with sabres'),
    ('Knights', 'armoured knights on barded horses charging with lances'),
    ('Catapult', 'a wooden catapult hurling a stone at a city wall'),
    ('Cannon', 'bronze cannons firing on a battlefield'),
    ('Chariot', 'an ancient war chariot charging with a driver and an archer'),
    ('Armor', 'a column of tanks advancing across a plain'),
    ('Mech. Inf.', 'armoured personnel carriers with infantry'),
    ('Artillery', 'heavy artillery guns firing'),
    ('Fighter', 'jet fighters in flight'),
    ('Bomber', 'a heavy bomber in flight dropping bombs'),
    ('Trireme', 'an ancient trireme galley rowing along a coast'),
    ('Sail', 'a medieval sailing caravel at sea'),
    ('Frigate', 'an 18th-century frigate under full sail firing a broadside'),
    ('Ironclad', 'a 19th-century ironclad steam warship'),
    ('Cruiser', 'a steel cruiser warship at sea'),
    ('Battleship', 'a great battleship firing its main guns'),
    ('Submarine', 'a submarine running on the surface'),
    ('Carrier', 'an aircraft carrier with planes on its deck'),
    ('Transport', 'a large troop transport ship'),
    ('Nuclear', 'a nuclear missile rising from its silo'),
    ('Diplomat', 'a diplomat in a long coat presenting a sealed letter'),
    ('Caravan', 'a merchant caravan of camels laden with goods'),
]
UNIT_CELLS = [4, 21, 19, 14, 24, 15, 13, 18, 16, 17, 8, 9, 12, 26, 20, 6, 1, 22, 25, 23, 11, 0, 10, 5, 2, 3, 27, 7]

TERRAINS = [
    ('Desert', 'a sand desert with dunes under a hot sun'),
    ('Plains', 'wide golden plains of tall grass'),
    ('Grassland', 'green rolling grassland'),
    ('Forest', 'a dense forest of tall trees'),
    ('Hills', 'grassy rolling hills'),
    ('Mountains', 'high rocky mountains with snowy peaks'),
    ('Tundra', 'cold barren tundra with moss and patches of snow'),
    ('Arctic', 'an arctic waste of ice and snow'),
    ('Swamp', 'a murky swamp with reeds and dead trees'),
    ('Jungle', 'a steaming tropical jungle'),
    ('Ocean', 'the open ocean with rolling waves'),
    ('River', 'a river winding through green land'),
]
TERRAIN_CELLS = [6, 7, 8, 9, 10, 11, 0, 2, 3, 1, 4, 5]


def _encyclopedia():
    sheets = {}
    for i, advance in enumerate(ADVANCE_CELLS):
        name = ADVANCES[advance]
        rect = ((i % 3) * 111 + 1, ((i % 9) // 3) * 69 + 1, 110, 68)
        sheets.setdefault(f'ICONPG{i // 9 + 1}', {'kind': 'scene', 'pieces': []})['pieces'].append(
            (rect, f'{ADVANCE_SCENES[name]} (the advance "{name}")'))
    for i, unit in enumerate(UNIT_CELLS):
        rect = ((i & 1) * 160 + 1, ((i % 6) >> 1) * 61, 158, 61)
        sheets.setdefault(f'ICONPG{"ABCDE"[i // 6]}', {'kind': 'scene', 'pieces': []})['pieces'].append(
            (rect, UNITS[unit][1]))
    for i, terrain in enumerate(TERRAIN_CELLS):
        rect = ((i % 3) * 107 + 1, ((i % 6) // 3) * 87 + 5, 106, 77)
        sheets.setdefault(f'ICONPGT{i // 6 + 1}', {'kind': 'scene', 'pieces': []})['pieces'].append(
            (rect, 'a landscape seen from ground level, with a horizon and sky: ' + TERRAINS[terrain][1]))
    return sheets


# City view buildings, cell i is improvement i + 2; CITYPIX2 before Combustion, CITYPIX3 after it. Cells 20 and 21 are
# not drawn and keep their stand-in art. The city view also repeats some cells: 17 as a row along the top (power plant),
# 22 along the city walls, and 15 along the mass transit line (it is also SDI Defense's cell).
BUILDINGS = [
    ('Barracks', 'a barracks with a drill yard'), ('Granary', 'a granary storehouse with sacks of grain'),
    ('Temple', 'a temple with columns'), ('Marketplace', 'a market square with stalls and awnings'),
    ('Library', 'a library building with a domed reading room'), ('Courthouse', 'a courthouse with steps and columns'),
    ('City Walls', 'a fortified gatehouse with two towers'), ('Aqueduct', 'a section of aqueduct on tall arches'),
    ('Bank', 'a bank building with a columned front'), ('Cathedral', 'a cathedral with a tall spire'),
    ('University', 'a university college with a clock tower'),
    ('Mass Transit', 'a transit station'), ('Colosseum', 'a round colosseum arena'),
    ('Factory', 'a factory with chimneys'), ('Manufacturing Plant', 'a large manufacturing plant with several halls'),
    ('Mass Transit line', 'a straight section of elevated rail track on pillars'),
    ('Recycling Center', 'a recycling center with sorting sheds and heaps'),
    ('Power Plant', 'a power plant with tall smoking chimneys'), ('Hydro Plant', 'a hydroelectric dam with a power house'),
    ('Nuclear Plant', 'a nuclear power plant with a domed reactor and a cooling tower'),
    None, None,
    ('City Walls section', 'a straight section of high city wall with a square tower'),
]

HOUSE_ERAS = [
    'a primitive hut of mud and thatch', 'a small timber house with a thatched roof',
    'a stone town house with a tiled roof', 'a large brick town house with chimneys', 'a modern apartment block',
]

WONDERS = [
    None,
    ('Pyramids', 'three great pyramids side by side in a wide, low row'),
    ('Hanging Gardens', 'terraced hanging gardens overflowing with greenery'),
    ('Colossus', 'the Colossus, a giant bronze statue standing over a harbour, seen from afar in a wide view'),
    ('Lighthouse', 'the great lighthouse of Alexandria, a tall stone tower with a fire on top'),
    ('Great Library', 'a great library with columns and a portico'),
    ('Oracle', 'the oracle temple, a round columned shrine'),
    ('Great Wall', 'a stretch of the Great Wall with watchtowers winding over hills'),
    ('Magellan\'s Expedition', 'a harbour with a great sailing ship moored at the quay'),
    ('Michelangelo\'s Chapel', 'a great domed renaissance chapel'),
    ('Copernicus\' Observatory', 'an observatory tower with a domed roof'),
    ('Shakespeare\'s Theatre', 'a round timber playhouse with a thatched roof'),
    ('Isaac Newton\'s College', 'a college building with a clock tower and a quadrangle'),
    ('J.S. Bach\'s Cathedral', 'a baroque cathedral with twin towers'),
    ('Darwin\'s Voyage', 'a survey sailing ship at anchor with a small boat beside it'),
    ('Hoover Dam', 'a great concrete dam seen from the side in a very wide, low view'),
    ('Women\'s Suffrage', 'a statue of a woman holding a banner on a plinth'),
    ('Manhattan Project', 'a laboratory complex with a mushroom cloud far behind it'),
    ('United Nations', 'a tall modern glass headquarters tower with flags in front'),
    ('Apollo Program', 'a rocket on its launch tower'),
    ('SETI Program', 'a large radio telescope dish'),
    ('Cure for Cancer', 'a modern hospital research centre'),
]
# corners (x1, y1, x2, y2) from the game's table; WONDERS2 holds the ones in WONDERS2_IDS, WONDERS the rest
WONDER_CORNERS = [None, (131, 54, 318, 82), (159, 149, 227, 198), (88, 97, 211, 135), (229, 116, 268, 198),
                  (61, 117, 101, 172), (164, 97, 227, 147), (1, 38, 66, 118), (268, 53, 318, 114), (9, 117, 59, 172),
                  (126, 1, 182, 53), (276, 1, 318, 51), (103, 139, 157, 198), (184, 1, 224, 69), (40, 69, 101, 115),
                  (1, 14, 147, 33), (253, 1, 274, 47), (226, 1, 251, 47), (103, 79, 162, 137), (270, 116, 318, 198),
                  (63, 1, 124, 54), (240, 60, 266, 114)]
WONDERS2_IDS = {1, 3, 7, 15}

# Advisors, by government and era: GOVT0 despotism and anarchy, GOVT1 monarchy, GOVT2 republic and democracy,
# GOVT3 communism (only A); A before Invention, M after it. Each has the four advisors as full figures (78x99 at
# 80i + 1, 101) and as heads (40x60 at 40i, 0).
ADVISORS = ['defense minister', 'domestic advisor', 'foreign minister', 'science advisor']
GOVERNMENTS = {
    '0A': 'under an ancient despot, in rough bronze-age dress', '0M': 'under a modern dictatorship, in severe uniforms and dark suits',
    '1A': 'at an ancient royal court, in rich robes', '1M': 'at a royal court of the age of kings, in powdered wigs and brocade',
    '2A': 'of an ancient republic, in togas', '2M': 'of a modern democracy, in suits',
    '3A': 'of a communist state, in plain uniforms and grey suits',
}
ADVISOR_LOOKS = ['in armour or uniform', 'holding a ledger', 'holding a sealed scroll', 'holding a book and instruments']


# the leaders by KING file number (the game's table of nations to files)
LEADERS = [('Queen Elizabeth I', 'English'), ('Pharaoh Ramesses', 'Egyptians'), ('Mahatma Gandhi', 'Indians'),
           ('King Shaka', 'Zulus'), ('Abraham Lincoln', 'Americans'), ('Genghis Khan', 'Mongols'), ('Mao Tse Tung', 'Chinese'),
           ('King Hammurabi', 'Babylonians'), ('Joseph Stalin', 'Russians'), ('Emperor Montezuma', 'Aztecs'),
           ('Julius Caesar', 'Romans'), ('Emperor Napoleon', 'French'), ('Frederick the Great', 'Germans'),
           ('Alexander the Great', 'Greeks')]


def _city():
    sheets = {}
    for name, era in (('CITYPIX2', 'before the industrial age'), ('CITYPIX3', 'of the industrial and modern age')):
        pieces = []
        for i, building in enumerate(BUILDINGS):
            if building:
                pieces.append((((i >> 2) * 50 + 1, (i & 3) * 50 + 1, 49, 49), f'{building[1]}, {era}'))
        sheets[name] = {'kind': 'object', 'pieces': pieces}
    houses = []
    for era, house in enumerate(HOUSE_ERAS):
        x = era * 64
        for k, (dx, dy) in enumerate(((1, 1), (33, 1), (1, 33), (33, 33))):
            houses.append(((x + dx, dy, 31, 31), f'{house}, variation {k + 1} of 4'))
    sheets['CITYPIX1'] = {'kind': 'object', 'pieces': houses}
    for name, ids in (('WONDERS', [i for i in range(1, 22) if i not in WONDERS2_IDS]), ('WONDERS2', sorted(WONDERS2_IDS))):
        pieces = []
        for i in ids:
            x1, y1, x2, y2 = WONDER_CORNERS[i]
            pieces.append(((x1, y1, x2 - x1 - 1, y2 - y1 - 1), f'{WONDERS[i][1]} (the wonder "{WONDERS[i][0]}")'))
        sheets[name] = {'kind': 'object', 'pieces': pieces}
    for govt, court in GOVERNMENTS.items():
        # each advisor also has a 40x60 head and three 28x24 small heads (one per expression; all the same here)
        sheets[f'GOVT{govt}'] = {'kind': 'figure', 'pieces': [
            ((80 * i + 1, 101, 78, 99), f'the {advisor} {court}, {ADVISOR_LOOKS[i]}',
             [(40 * i, 0, 40, 60)] + [(161 + 29 * k, 25 * i + 1, 28, 24) for k in range(3)])
            for i, advisor in enumerate(ADVISORS)]}
    for number, (leader, nation) in enumerate(LEADERS):
        # the face parts are laid over the bust for expressions; they would not fit a new bust, so they are cleared
        parts = [(60 * (j % 5) + 1, 50 * (j // 5) + 1 + dy, 59, h) for j in list(range(8)) + [10, 11, 12, 15, 16, 17]
                 for dy, h in ((0, 20), (20, 29))]
        sheets[f'KING{number:02}'] = {'kind': 'bust', 'clear': parts, 'pieces': [
            ((181, 67, 139, 133), f'{leader}, leader of the {nation}, in the dress of a ruler, facing the viewer')]}
    return sheets


SHEETS = {**_encyclopedia(), **_city()}
SHEETS['POP'] = {'kind': 'figure', 'pieces': [((1 + (i % 9) * 35, 1 + (i // 9) * 51, 34, 50), s) for i, s in enumerate(POP)]}
