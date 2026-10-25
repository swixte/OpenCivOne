# Art inventory

Everything the game draws, and what a theme has to supply for it. The game reads 106 pictures (`.PIC`, each 320x200
pixels, 256 colors), 38 palettes (`.PAL`) and 13 text files (`.TXT`). Nothing else is needed: the stand-in data set
(`tools/standin`) contains no original files, and the game plays on it.

Pictures fall into three kinds, which matter for making new art:

- **Backdrops** are shown whole. New art only has to keep the picture's areas: where text is written over it, or where
  the game draws something on top.
- **Sheets** are cut into pieces at fixed places, and the game assembles screens from the pieces. New art has to put each
  piece in its exact rectangle. Coordinates below are in the 320x200 original; HD art is 2x or 4x of them
  (see [hd-art.md](hd-art.md)).
- **Patterns** are small tiles repeated over an area.

Status: **done** = painted set exists (`assets/hd4`, `assets/painted`); **placeholder** = only the stand-in.

The code locations are where each file is loaded and cut, for checking details.

## Map and interface sheets

| File | Kind | Contents | Status |
| --- | --- | --- | --- |
| TER257 | sheet | Terrain with neighbour variants, ocean quarters, river mouths. Layout in [hd-art.md](hd-art.md#sheet-layout). | done (replaced by the painted map) |
| SP257 | sheet | Units, roads, railroads, rivers, irrigation, mines, pollution, resources, city, walls, fortress, hut, fog, explosions. Layout in [hd-art.md](hd-art.md#sheet-layout). | done |
| SP299 | sheet + patterns | Coasts, grass background, interface patterns (stone 32x16 at 288,120; city screen 16x16 at 208,100), and the small sprites below. | patterns done, small sprites placeholder |

Small sprites cut in `StartGameMenu.cs` (`F5_0000_1455_LoadBitmaps`), not yet painted:

| Sheet | Rectangle | Count | Use |
| --- | --- | --- | --- |
| SP299 | x = 160 + 42i, y = 142, 41x58 | 3 | portraits beside report boxes (spies, diplomats, travelers) (`MenuBoxDialog.cs`) |
| SP257 | x = 160 + 7i, y = 0 / 16 / 32, 7x15 | 3 rows of 20 | small palace parts, three styles (`GameEngine.cs`) |
| SP257 | x = 129 + 8(i & 3), y = 33 + 2(i & 4), 7x7 | 8 | city screen resource icons: food, shields, trade, and the rest (`CityWorker.cs`) |
| SP257 | x = 8i, y = 128, 8x16 | 9 | city screen citizens, ancient and modern, and the disorder sign (`CityWorker.cs`) |
| SP257 | 176, 128, 20x9 | 1 | figure on the news screen (`News.cs`) |
| SP257 | 48, 32, 16x16 | 1 | marker in the city screen's lower panel (`CityWorker.cs`) |

## Picture sheets

| File | Rectangles | Contents | Code |
| --- | --- | --- | --- |
| CITYPIX1 | 4 groups of 4 houses, 31x31, at (1 + x, 1), (33 + x, 1), (1 + x, 33), (33 + x, 33); roads 24x8 at y 65 and 81 | city view houses and streets, by era | `CityView.cs` `LoadCityViewBitmaps` |
| CITYPIX2, CITYPIX3 | 49x49 at (50(i >> 2) + 1, 50(i & 3) + 1), cell i = improvement i + 2 | city view buildings, before and after Combustion; CITYPIX2 is also the Encyclopedia picture for improvements. Cell 17 is repeated along the top (power plant), 22 along the city walls, 15 along the mass transit line | same |
| WONDERS, WONDERS2 | 21 fixed rectangles (`wonderRectangles` in `CityView.cs`, corners; width x2 - x1 - 1); WONDERS2 holds Pyramids, Colossus, Great Wall and Hoover Dam | city view wonders | `CityView.cs` |
| ICONPG1–8 | 110x68 at (111(i % 3) + 1, 69((i % 9) / 3) + 1), 9 per sheet, cell order in `technologyAdvanceFileID` | advance pictures (Encyclopedia, discovery screen) | `Encyclopedia.cs`, `GameEngine.cs` |
| ICONPGA–E | 158x61 at (160(i & 1) + 1, 61((i % 6) >> 1)), 6 per sheet, order in `unitTypeFileID` | unit pictures (Encyclopedia) | `Encyclopedia.cs` |
| ICONPGT1–2 | 106x77 at (107(i % 3) + 1, 87((i % 6) / 3) + 5), 6 per sheet, order in `terrainTypeFileID` | terrain pictures (Encyclopedia) | `Encyclopedia.cs` |
| POP | 34x50 at (35i + 1, 1) and (35i + 1, 52) | city population figures (content, happy, unhappy, specialists), two eras | `CityView.cs` `DrawCityPopulation` |
| KING00–13 | face parts: 59x20 at (x, y + 1), 59x29 at (x, y + 21); expressions 28x24 at (161 + 29k, 25n + 1) | the 14 leaders, assembled from parts for each expression | `MeetWithKing.cs` |
| GOVT0A–3A, GOVT0M–2M | 4 advisors (defense, domestic, foreign, science): figures 78x99 at (80i + 1, 101), heads 40x60 at (40i, 0) | 0 despotism and anarchy, 1 monarchy, 2 republic and democracy, 3 communism; A before Invention, M after | `MeetWithKing.cs`, `StartGameMenu.cs` |
| CASTLE0–4 | 3 x 5 parts per level, rectangles in `Palace.cs` (`bitmapRectangles`) | palace pieces for the five building levels | `Palace.cs` |
| CBACK, CBACKS1–3, CBRUSH0–5 | backdrop plus garden and brush layers | palace grounds | `Palace.cs` |
| SETTLERS | 47x15 at (1, 16i + 1), 4 frames | settlers walking (new city animation) | `Animations.cs` |
| NUKE1 | 44x44 at (45(i % 7) + 1, 45(i / 7) + 1) | nuclear explosion frames | `Animations.cs` |
| INVADERS, INVADER2, INVADER3 | 78x60 / 78x65 grids, 4 per row | city under attack animation | `CityView.cs` |
| RIOT, RIOT2 | 78x63 / 74x65 grids, 4 per row | civil disorder animation | `CityView.cs` |
| LOVE1, LOVE2 | 78x65 grid, 4 per row | "We love the king" day animation | `CityView.cs` |
| PLANET1, PLANET2 | 49x49 grid, 6 per row | space arrival animation | `MainIntro.cs` |
| SPACEST | sheet | spaceship parts | `MainIntro.cs`, spaceship screens |
| SLAM1, SLAM2 | 59x49 at (1, 51) and (1, 151); backdrop | defeat and replay scenes | `Replay.cs` |
| DOCKER | 10 rectangles from the game's data table (DS:4BE2) | space docking parts | `Overlay_18.cs` |
| LOGO | 320x80 band at (0, 64) | title logo | `MainIntro.cs` |
| TORCH | small | intro torch | `StartGame.cs` |
| DIFFS | 5 crowns, 53x47 at (21, 35i + 6) for even i and (80, 35i + 6) for odd i; also a backdrop | one crown per difficulty level, copied beside the next menus; clicking left of x 135 picks the level by row | `StartGameMenu.cs` |

## Backdrops

| Files | Shown for |
| --- | --- |
| BIRTH0–8 | intro story and "the dawn of civilization" sequence |
| BACK0A–3A, BACK0M–2M | backgrounds behind leaders and advisors, by era |
| HILL | city founding and city view landscape |
| CUSTOM, DIFFS | world customisation and difficulty choice; menus are written on the right in dark text, from x 200 (CUSTOM) and x 160 (DIFFS) |
| ARCH | game over (lost) |
| DISCOVR1, DISCOVR2 | discovery announcements (`GameEngine.cs`) |
| MAP | the Earth map for "play on Earth" (`MapManagement.cs` `LoadEarthMap`) |
| SAD | diplomacy and palace scenes (`Diplomacy.cs`, `Palace.cs`) |

SLAG2, ADSCREEN, KINK00, KINK03 and SPRITES are on the list of files the game checks for at start, but no code draws them
yet, so a theme only needs a file of the right size.

## Palettes

38 `.PAL` files, one for each backdrop or sheet that has its own colors (BACK*, BIRTH*, GOVT* and others). HD art
(true color) does not need them, but the game still loads them, so a theme keeps a palette per picture.

## Text

| File | Contents |
| --- | --- |
| HELP, BLURB0–4 | Civilopedia and help text |
| KING | leader dialogue |
| INTRO, INTRO3, STORY | intro story |
| PRODUCE | production advice |
| CREDITS, ERROR | credits, error messages |

Many names are written in the code rather than in these files: units, advances, improvements, wonders, governments,
civilizations and leaders, and the menus. Renaming them for a theme means moving them into a data file first.

## What a theme needs

`tools/hdart/theme.py` generates a theme (see [hd-art.md](hd-art.md#themes)); `tools/hdart/pictures.py` says what each
picture shows; every sheet above except the leaders, palace, spaceship, roads and animations is described there.
Done so far: the map and interface, BIRTH0, CUSTOM, DIFFS, HILL, POP, GOVT0A and ICONPGT1.

- **Map and interface**: done, generated from one concept image (see [hd-art.md](hd-art.md)).
- **About 60 backdrops**: one generated picture each, keeping its text areas.
- **About 30 picture sheets**: generated piece by piece and placed into their rectangles. Leaders, the palace and the
  city view need the most care: they are assembled from many parts that have to fit together.
- **Text**: the 13 files, and the names in the code once they are moved out.
