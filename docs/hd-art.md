# HD art

The game can show 2x art in place of the original 320x200 graphics. Nothing changes unless HD images are present, so the default look stays the original one.

## Playing with the HD map art

This repository includes a complete 2x set for the map: terrain with its neighbour variants, coasts, rivers, roads, railroads, improvements, special resources, cities, fog and all 28 units, in `assets/hd`.

1. Build the game with the .NET 10 SDK (`dotnet build OpenCivOne.sln`). The project targets .NET 8, but its Avalonia version needs the newer compiler.
2. Put the original DOS Civilization files where the game looks for them: next to the executable for a release build, or `~/Dos/Civ1` (`C:\Dos\Civ1` on Windows) for a debug build.
3. Copy the contents of `assets/hd` into an `hd` folder there, so you have for example `Civ1/hd/TER257/0_0.png`.

To change the art, edit the generator in `tools/hdart` and run `python3 tools/hdart/generate.py` (needs numpy and Pillow). `python3 tools/hdart/preview.py out.png --scene` renders a sample map with the same tile choices the game makes.

## How it works

Every PIC file the game loads can have HD versions in the `hd` folder:

- `hd/NAME.png`: the whole image at twice the size (a 320x200 PIC needs a 640x400 PNG).
- `hd/NAME/X_Y.png`: one region, with its top left corner at X, Y in the original image and twice the region's size. Regions are applied over the whole image, and anything not covered keeps the original art. A region also rewrites the original pixels under it to match the HD art (transparent, the key color index, or the closest palette color), so transparency and color swaps follow the new art.

Sprites cut from a sheet carry their part of the HD image with them, so when the game draws a terrain tile or a unit from that sheet, the HD pixels are drawn. Anything drawn without HD art, such as text, menus, boxes and lines, falls back to the original pixels, upscaled with nearest-neighbour.

- Transparent PNG pixels are transparent when the sprite is drawn, and partly transparent pixels are blended (drop shadows).
- HD art can draw where the original sprite is transparent, as long as it stays inside the sprite's original rectangle.
- An image with the wrong size is ignored.

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
