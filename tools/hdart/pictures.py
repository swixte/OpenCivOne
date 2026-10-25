"""What each of the game's pictures shows, for generating a theme (see theme.py and docs/art-inventory.md).

BACKDROPS are shown whole: one generated image each. GRIDS are sheets the game cuts into equal pieces: each piece is
generated as a single object and placed into its rectangle. A theme can change any subject in its theme.json.

Pictures not listed here (leaders, advisors, palace, city view, wonders, Encyclopedia pages, animations) are assembled
from many fitted parts and keep their stand-in art for now. MAP is the Earth's terrain stored as pixels: never paint it.
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

# Grid sheets: cell size, top left of the first cell, step between cells, and one subject per cell (row by row).
GRIDS = {
    'POP': {
        'cell': (34, 50), 'origin': (1, 1), 'step': (35, 51),
        'items': [
            # top row: the modern era (after Industrialization)
            'a happy modern townsman, cheering', 'a happy modern townswoman, waving',
            'a content modern townsman, standing calmly', 'a content modern townswoman, standing calmly',
            'an unhappy modern townsman, arms crossed', 'an unhappy modern townswoman, frowning',
            'a modern entertainer with a guitar', 'a modern tax collector with a ledger', 'a modern scientist in a lab coat',
            # bottom row: the ancient era
            'a happy ancient townsman in a tunic, cheering', 'a happy ancient townswoman in a robe, waving',
            'a content ancient townsman in a tunic', 'a content ancient townswoman in a robe',
            'an unhappy ancient townsman in a tunic, arms crossed', 'an unhappy ancient townswoman in a robe, frowning',
            'an ancient entertainer playing a lyre', 'an ancient tax collector with a scroll', 'an ancient scholar with a scroll',
        ],
        'columns': 9,
        'standing': True,
    },
}

NEVER_PAINT = {'MAP'}
