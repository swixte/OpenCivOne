# HD art

The game can show 2x art in place of the original 320x200 graphics. Nothing changes unless HD images are present, so the default look stays the original one.

## Playing with the HD map art

This repository includes a complete 2x set for the map: terrain with its neighbour variants, coasts, rivers, roads, railroads, improvements, special resources, cities, fog and all 28 units, in `assets/hd`.

1. Build the game with the .NET 10 SDK (`dotnet build OpenCivOne.sln`). The project targets .NET 8, but its Avalonia version needs the newer compiler.
2. Put the original DOS Civilization files where the game looks for them: next to the executable for a release build, or `~/Dos/Civ1` (`C:\Dos\Civ1` on Windows) for a debug build.
3. Copy the contents of `assets/hd` into an `hd` folder there, so you have for example `Civ1/hd/TER257/0_0.png`.

### Without the original game files

`tools/standin/build.sh [FOLDER]` builds a stand-in data set, so the game starts and plays without the DOS files (default folder `~/Dos/Civ1`, where a debug build looks). It needs Python with numpy and Pillow and the .NET 10 SDK. Nothing in it comes from the original game:

- the map sheets (TER257, SP257, SP299) are the HD art reduced to the original size, and the HD set is copied into its `hd` folder;
- every other picture is a labelled placeholder, and the interface patterns and icons are simple shapes;
- the text files hold one generic sentence under every key the code asks for.

It is meant for working on the engine and the art; for the real game, use the original files.

To change the art, edit the generator in `tools/hdart` and run `python3 tools/hdart/generate.py` (needs numpy and Pillow). `python3 tools/hdart/preview.py out.png --scene` renders a sample map with the same tile choices the game makes.

### Art from images

`python3 tools/hdart/generate.py OUT --style PACK` builds the set from a folder of images instead of drawing it: a texture image for each terrain, made to repeat without seams, and sprites for trees, hills, mountains, jungle, cities, huts and units, which get their backgrounds removed, are scaled to the tile and get drop shadows. Units also get a small banner in the player's colors. Anything the folder lacks is drawn as before. `tools/hdart/style.py` lists the file names; `preview.py` takes the same `--style PACK`.

## Painted map

With an `hd/painted` folder, the map is no longer built from terrain tiles. The game paints the whole world as one picture from its terrain:

- the ground textures blend across terrain borders with soft, irregular edges;
- the sea is shallow and teal near land, deep away from it, and breaks into surf along the coast;
- rivers wind through river cells and on into the sea;
- trees, jungle, hills and mountains are scattered over their cells and reach into the cells around them, back to front, with soft shadows;
- drifting cloud shadows and a colour grade lie over everything;
- the sea and rivers move, as the original game's palette-cycled water did: a slow swell on the sea and a ripple
  running along rivers, about ten frames a second. Trees, peaks, units and cities on the water stay still. Set
  `"AnimateWater": false` in `~/Games/OpenCivOne/Preferences.json` to keep the water still.

Each map cell shows its square of that painting. Irrigation, roads, mines, resources, huts, cities, units and fog borders are drawn over it as before, from the HD set or the original sprites. The world is painted in chunks of 8x8 cells: the chunk a cell needs is painted when it is first shown (about a fifth of a second), and from then on a background thread paints the rest of the world on one core, nearest chunks first, so scrolling rarely waits. A chunk is painted again when the terrain under it changes. The same cell always gets the same trees and peaks.

The painting works at either HD scale; at 4x (`scale.txt` with `4`) it is 64 pixels per cell.

### The 4x set

`assets/hd4` is the sprite set that goes with the painted map at 4x: all 28 units, the special resources, the city, city walls, fortress and huts are painted in the same style, and the rest (roads, railroads, irrigation, mines, pollution, fog borders, explosions) are the drawn 2x sprites at twice the size. Units carry a small flag in their player's colors, and the city a badge in its player's colors. It has no terrain tiles, since the painting replaces them, and it holds `scale.txt` with `4`.

To play with it, copy `assets/hd4` to `Civ1/hd` and `assets/painted` to `Civ1/hd/painted` (`HD_SET=hd4 tools/standin/build.sh` does this for the stand-in set). With the 2x set in `assets/hd` instead, the map is painted at 32 pixels per cell and the other sprites are the drawn 2x ones.

On the painted map the city size is written in the top left corner of the city, on the badge, so that the town shows.

`tools/hdart/painted_set.py` builds the set from the painted objects in `assets/painted/objects`.

### Print style

An optional `style.txt` in the painted art folder (lines of `name value`) changes how the art is painted:

- `print 1` paints the map like a woodblock print: each terrain is an area with an organic edge and rounded corners
  (each pixel takes the terrain that the nearby cells vote for, weighted by distance, so the square cells do not show),
  and neighbouring terrains fade into each other over a few pixels. Forest and jungle are built from tree crowns
  packed edge to edge wherever the rounded terrain is wood, so the crowns make the scalloped edge of the wood. The coast
  is a crisp ink line with a narrow cream beach inside it; the sea lightens towards the shore in a soft gradient that
  follows the coastline, with one broken wave line along it. Rivers flow in smooth curves from side to side of their
  cells, swinging in S-bends at an even width; with a `river.png` strip in the art folder (a straight painted river,
  made from a generated one by `painted_assets.py`), the strip is laid along each course, inside a fine ink line and a
  pale bank, as the mockup draws them; without one, a plain blue ribbon is drawn. Branches of a fork meet in one point.
  Resources and huts are painted into the scenery (the game passes them in with `FeatureAt` and then does not draw
  them over the map): back to front with the trees and peaks, in a clearing in a wood, at the foot of hills and
  mountains, which stay low beside and in front of them, so they belong to the ground. Scenery never reaches into the sea in front
  of it (a peak may rise against the sea behind it), trees keep out of rivers, the feet of hills and mountains fade into
  the ground, nothing casts a shadow, and grassland gets more, smaller trees.
- `pattern`, `seapattern`: how much larger the land and sea textures are drawn (smaller than 1 draws them smaller);
  `pattern forest 1.3` sets one terrain's own (by its texture name).
- `paper`: how much colours lean towards cream paper.
- `clouds`, `grade`: the strength of the cloud shadows and the warm colour grade (1 each); `shallows`: the width of the
  shallow water; `coastline 1`: the printed coast in the painterly mode too.

A terrain can also have a ground texture of its own: `forest`, `hills`, `mountains` and `jungle` in `ground/` replace
the mix those terrains use otherwise. `themes/ukiyo-e/hd/painted` uses all of this.

With `print 1`, `painted_set.py` also draws units and cities as print tokens (a dark key line and a cream outline, no
shadows), resources and huts as part of the ground (only a thin ink line), and the shield marker as a red seal stamped
into the paper, and the interface on paper: cream washi behind the sidebar, menus and dialogs, and a mid indigo behind the
city screen, from the textures in the theme's `hd/painted/ui` (`stone`, `slate`, `grain`) when it has them.

### Art

The repository includes the art in `assets/painted`; copy it to `Civ1/hd/painted` (the stand-in build does this). The folder holds:

- `ground/NAME.png`: textures that repeat without seams, one for each of `grass`, `plains`, `desert`, `tundra`, `snow`, `ocean`, `shallow`, `swamp` and `floor` (under trees). A texture spans 8 cells. Each terrain uses one or two of them; a missing one shows as flat grey-green.
- `trees/`, `jungle/`, `hills/`, `mountains/`: sprites with transparent backgrounds, any size; the game scales them. Forest cells get about 26 trees, jungle 12 crowns, hills 2 hills, mountains one peak about 1.4 cells tall, and some grassland a lone tree.
- `objects/`: single painted objects (units, resources, town, walls, fortress, hut) that `painted_set.py` uses; the game does not read them.

The included art was generated from a concept image with `tools/hdart/generate_painted.py` (OpenAI Images API, the concept as a style reference; set `OPENAI_API_KEY`). Sprites and objects come back on flat magenta, and `tools/hdart/painted_assets.py` makes the textures seamless and cuts the sprites and objects out. To see the painting without the game, run the HD layer tests with `PAINTED_PREVIEW=out.png`: they paint a sample map with the art in `assets/painted`.

## Painted interface

The 4x set also paints the interface:

- The stone pattern behind the sidebar, status panel, menus and dialogs (SP299 32x16 at 288, 120) and the city screen background (SP299 16x16 at 208, 100) are painted textures. The stone is light enough for the black text on it.
- With `hd/ui/material.png`, flat boxes on the main screen (dialogs, notes, city screen panels, buttons, bars) get a painted grain and darker edges, and boxes and lines are toned to the painted look: their palette colors are half desaturated and a little darker and warmer, so the bright EGA blues, greens and reds keep their hue but sit with the art. The grain is stored as HD pixels that take their color from the palette when shown, so menu highlights, color swaps and palette fades still work. Text drawn on a box keeps its exact color. Fills smaller than 6x6 pixels get the tone but no grain. The texture is a square grey image, mid grey (128) keeps a color, darker and lighter shade it.
- With `hd/ui/colors.txt`, a theme gives the interface its own colors: each line is a palette index and the color that replaces it as `RRGGBB` (`#` starts a comment). On the main screen, boxes, lines and text of those indexes take the color as it is (boxes keep their grain) instead of the painted tone, and index 5 also colors the frame around the game. Player colors on HD sprites are not affected. The ukiyo-e theme turns black and the magenta frames into sumi ink, the blue panels and buttons into indigo, white text into cream and the reds into madder and vermilion.
- With `hd/upscale.txt` containing `scale2x`, everything without HD art (text, lines, the minimap, pictures) is enlarged with Scale2x (Scale4x at 4x) instead of nearest-neighbour, which rounds the steps in text and diagonals while keeping the colors.

- With the material, everything else on the main screen without HD art (text, the minimap, pictures) gets a gentler version of the same tone (a third desaturated, a little warmer), so the original colors sit with the painted art without losing their meaning.

`painted_set.py` writes all of this from the textures in `assets/painted/ui`.

### Full-screen pictures

The advisors, leaders, city view, intro and other pictures come from the original game files, so they cannot be painted without them. `tools/hdart/repaint_pictures.py` repaints them from your own copy:

1. Set `"ExportHDTemplates": true` in the `Debug` section of `~/Games/OpenCivOne/Preferences.json` and play for a while: every picture the game loads is written to `~/Games/OpenCivOne/hd-templates`.
2. Run `OPENAI_API_KEY=... python3 tools/hdart/repaint_pictures.py ~/Games/OpenCivOne/hd-templates CONCEPT.png OUT`. Each full-screen picture is sent to the OpenAI Images API with the concept image, asking for the same picture painted in its style with the composition and lettering kept, and is written as `OUT/NAME.png` at the HD size. `--only NAME ...` limits it to some pictures (and also allows ones it would skip).
3. Copy the files into the `hd` folder and look at each in the game; delete a file to go back to the original.

Sprite sheets (map tiles, units, city buildings, leader faces, wonders and the like) are skipped, since the game cuts them into pieces at fixed places that a repainted sheet would not keep.

## Themes

A theme is a complete set of HD art, kept in the game's `themes/NAME` folder (next to the PIC files) and chosen with
`"Theme": "NAME"` in `~/Games/OpenCivOne/Preferences.json`. Without a theme, or when its folder is missing, the game uses
the `hd` folder. Switching themes needs only a restart, so a game can have several looks side by side.

`tools/hdart/theme.py` makes a theme from one concept image and a style description. The source of a theme is a folder
in `themes/` with a `theme.json`:

```json
{
  "name": "dark-painterly",
  "concept": "concept.png",
  "style": "dark, moody, painterly, muted fantasy strategy map, light from the top left",
  "quality": "medium",
  "pictures": {}
}
```

- `OPENAI_API_KEY=... python3 tools/hdart/theme.py build themes/NAME` generates the theme's pictures into
  `themes/NAME/hd` (OpenAI Images API, the concept as a style reference). What each picture shows is in
  `tools/hdart/pictures.py`; `pictures` in `theme.json` replaces a subject for one theme. `--only NAME ...` builds some
  pictures, `--force` builds them again, and `--map` also generates the painted map, units and interface in the
  theme's style (about 60 images).
- `python3 tools/hdart/theme.py install themes/NAME [GAME_DIR]` puts it into `GAME_DIR/themes/NAME` (default
  `~/Dos/Civ1`): the shared 4x set (`assets/hd4`, `assets/painted`) with the theme's own files over it, so a theme only
  holds what it changes.
- `python3 tools/hdart/theme.py list` shows the pictures it knows.

Pictures come in these kinds (see [art-inventory.md](art-inventory.md)):

- **Backdrops** are generated whole, at 1536x1024, and cropped to the 16:10 screen. Their subjects ask for calm areas
  where the game writes text: menus written straight onto a picture use dark text, so those need a light area behind
  them.
- **Placed objects** are generated alone and put into the exact rectangles the game copies out of a backdrop, with a
  soft shadow. DIFFS uses this for its five crowns.
- **Sheets** are cut by the game at fixed rectangles. Each piece is generated alone and written as a region file
  (`NAME/X_Y.png`), so a piece a theme does not paint keeps its stand-in art. A piece is a *scene* (an opaque
  illustration filling its rectangle, written as `.jpg`: the Encyclopedia pictures), an *object* (cut out, resting on
  the bottom of its rectangle: city buildings, houses, wonders), a *figure* (a cut-out person: citizens, advisors) or
  a *bust* (a leader). An *animation* is one generated group whose frames are made from it, so they stay the same
  figures: crowds and settlers bob as they walk, the nuclear blast grows and fades. A palace's right-hand walls and
  towers are its left-hand ones mirrored. An advisor is generated once and used for its full figure and, cut to head and shoulders, for
  its portraits. A leader is one still bust: the face parts the game lays over it for expressions would not fit a new
  bust, so they are written as transparent regions.

A theme can give each kind of asset (backdrop, scene, object, figure, bust) its own style reference images and its own
rules in `theme.json` (`references` and `rules`); the prompts then ask to take only the painting style, palette and
light from the references. `--only NAME:i` builds single pieces of a sheet, for trying a style on a few assets.

Each object is generated on flat magenta and cut out. The generated pieces are kept in `themes/NAME/pieces`, so
`--force` composes a picture again without new images; delete a piece to generate it again.

Pictures without transparency can be `.jpg` instead of `.png`, for whole images and regions alike; a full theme is
several times smaller that way.

The repository has one theme, `themes/dark-painterly`. Done so far: all the intro, throne room, discovery and ruins
backdrops, CUSTOM, DIFFS, HILL, all 67 advance pictures, 12 of the 28 unit pictures (ICONPGA-B), six terrain pictures
(ICONPGT1), the citizens, the despotism advisors (GOVT0A) and Caesar (KING10). `theme.py list` shows all 24 backdrops and 56 sheets it can build. Each backdrop,
object or scene is one medium-quality image.

### Names

A theme can rename the units, advances, improvements, wonders, terrain, governments, ruler and realm titles (Emperor,
Comrade, Empire, Kingdom ...), difficulty levels and nations (leader, plural, adjective and city names) with a `names.json` in its `hd` folder; without a theme the game reads `names.json` from its
own folder. `themes/names.json` holds every original name mapped to itself: copy it, change the right-hand sides and
remove what you keep. Each entry maps an original name to its new one, and nations are keyed by their plural
(`"Romans": {"Leader": "...", "Nation": "...", "Nationality": "...", "Cities": [...]}`). Governments have two forms in the
game, as a type (Communist, Democratic) and as an Encyclopedia topic (Communism, Democracy), and both are listed.

Keep names about as long as the originals: the screens have room for them, and a save game stores unit names in 12
characters (a loaded game keeps the names it was saved with).

### Texts

A theme can also have its own copies of the game's text files (`BLURB0-4.TXT` for the Encyclopedia, `HELP.TXT`,
`KING.TXT`, `PRODUCE.TXT`, `STORY.TXT`, `CREDITS.TXT` and the rest) in its `hd` folder; the game reads a theme's copy
when there is one. Their topics keep the original names as keys (`*POTTERY`), also for renamed things: the game looks
a renamed topic up by its original name.


## How it works

Every PIC file the game loads can have HD versions in the `hd` folder:

- `hd/NAME.png`: the whole image at twice the size (a 320x200 PIC needs a 640x400 PNG).
- `hd/NAME/X_Y.png`: one region, with its top left corner at X, Y in the original image and twice the region's size. Regions are applied over the whole image, and anything not covered keeps the original art. A region also rewrites the original pixels under it to match the HD art (transparent, the key color index, or the closest palette color), so transparency and color swaps follow the new art.

Sprites cut from a sheet carry their part of the HD image with them, so when the game draws a terrain tile or a unit from that sheet, the HD pixels are drawn. Anything drawn without HD art, such as text, menus, boxes and lines, falls back to the original pixels, upscaled with nearest-neighbour.

- Transparent PNG pixels are transparent when the sprite is drawn, and partly transparent pixels are blended (drop shadows).
- HD art can draw where the original sprite is transparent, as long as it stays inside the sprite's original rectangle.
- An image with the wrong size is ignored.
- The art is twice the original size unless the `hd` folder has a `scale.txt` containing `4`: then every image is four times the size (64x64 tiles), the game renders at 1280x800 and the window opens at that size when the screen has room.

### Art above the tile

A region file named `X_Y_tN.png` is N original pixels taller than its sprite: its top 2N rows are drawn above the sprite, in the tile above. The included set uses this for mountains, hills, forest, jungle and cities, which gives the map a sense of height.

When any such art is loaded, the map is drawn so that it is not lost:

- the map draws all cells first and the units after them, so units stay in front of terrain from the cell below;
- drawing a single cell also draws the tall art of the cell below it again, and redraws the unit of the cell above;
- tall art is clipped to the map view, so the top row does not reach into the menu bar.

Without tall art loaded, the map is drawn exactly as in the original.

### Key colors

The game gives units and cities their player colors by swapping palette colors. HD art marks the parts that should change with two key colors:

- primary key: magenta shades, red = blue and no green, for example `#800080`;
- secondary key: cyan shades, green = blue and no red, for example `#008080`.

A key pixel shows the color of the original pixel under it. A channel value of 128 means exactly that color; lower is darker and higher is lighter, so shading survives. In region files the primary key stands for color index 10 and the secondary key for index 2, which are the indexes the game swaps to the player's colors on the unit row. A file name ending in `_kP-S` sets other indexes; the city uses `_k0-5`.

Sentried units draw their HD art in grey, as the game greys out their original pixels.

## Sheet layout

Coordinates are in the original 320x200 sheets.

| Sheet | Region | Sprites |
| --- | --- | --- |
| TER257 | x = 16 * variant, y = 16 * terrain | Desert, Plains, Grassland, Forest, Hills, Mountains, Tundra, Arctic, Swamp, Jungle. Rows 0-5 have 16 variants by matching neighbours (bit 0 N, 1 E, 2 S, 3 W); rows 6-9 use variant 0 only. Drawn over the grass background. |
| TER257 | x = 16 * v, y = 176, 16x16 | Ocean quarters. Each 8x8 quarter of an ocean tile is chosen by three neighbours (side, corner, side, clockwise); v is those three land bits. |
| TER257 | x = 128 + 16 * i, y = 176 | River mouths on ocean tiles, from N, E, S, W. |
| SP299 | x = 224 ... 304, y = 100 | Whole-tile coasts for neighbour masks 0x1c, 0xc1, 0x07, 0x70, 0x8f, 0xf8. |
| SP299 | 256, 120 | Grass background under every land tile. |
| SP257 | x = 16 * m, y = 64 and 80 | Rivers, m = connections (bit 0 N, 1 E, 2 S, 3 W) to rivers and ocean. |
| SP257 | x = 16 * i, y = 48; x = 128 + 16 * i, y = 96 | Roads and railroads toward direction i + 1 (N, NE, E, SE, S, SW, W, NW). |
| SP257 | 64, 32 / 80, 32 / 96, 32 | Irrigation, mines, pollution. |
| SP257 | x = 16 * i + 1, y = 113, 15x15 | Special resources by terrain (0-11), city (12, drawn at the cell's 1, 1), city walls (13), fortress (14), hut (15). |
| SP257 | 153, 41, 7x7 | Grassland shield bonus. |
| SP257 | x = 80 + 16 * i, y = 128 | Fog borders toward unexplored N, E, S, W. |
| SP257 | x = 16 * i + 1, y = 97, 15x15 | Explosion frames. |
| SP257 | x = 16 * (j % 20) + 1, y = 161 + 16 * (j / 20), 15x15 | Units in UnitTypeEnum order. |

## Starting point for artists

Set `"ExportHDTemplates": true` in the `Debug` section of `Preferences.json` (in `~/Games/OpenCivOne`) and play for a while. Every PIC the game loads is written to `~/Games/OpenCivOne/hd-templates` as a 2x nearest-neighbour PNG. Index 0 is written as transparent. Copying a template into the `hd` folder unchanged gives exactly the original look.

## Known limits

- Palette effects, such as fades and colour cycling, change only original pixels. HD pixels keep their colours.
- The city size and other text stay in the original pixel font.
- On the painted map, a tall peak or tree from an unexplored cell can show at the edge of an explored one.
