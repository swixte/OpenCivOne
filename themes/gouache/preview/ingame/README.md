# Gouache units and town in the real game

Screenshots from the real game (Debug build of `city-screen-exploration` at `7243766`). It ran headless under Xvfb with the stand-in data set and the gouache theme copied to `~/Dos/Civ1/themes/gouache`. Preferences were `{"Theme": "gouache", "AnimateWater": true, "Debug": {"DebugMode": true}}`. Debug mode reveals the map and shows the AI's units and cities.

The game window is 1280x800, and a map cell is 64 px on screen, so a 1x crop is what a player sees. Larger crops are nearest-neighbour enlargements of the same capture.

## How the shots were made

1. Started a new game with the default choices (Chieftain, 7 civilizations, Romans, so the human player is white) and saved straight away.
2. `place_units.py` (in this folder) edited the save. It put militia, phalanx, legion, knights, catapult, chariot, a second settlers, trireme and sail beside the starting settlers, plus musketeers and cannon in the old 4x art for comparison. It also moved the Babylonians' (player 2, green) settlers and five of their units a few cells east.
3. Loaded the save, founded Rome with the settlers' **B** key, then moved every unit one step so the map drew it (see "Notes on running" below). After that I pressed space through four turns. The Babylonians founded a city on the coast, and the French had founded Paris on their own island.

## The shots

| File | What it shows |
| --- | --- |
| `01_turn2_overview.png` | Whole window, 3980 BC. Roman units in two rows north of Rome, both ships south of it, Babylonian units on the right. |
| `02_units_1x.png`, `03_units_2x.png` | The same group at game size and at 2x. |
| `04_roman_units_3x.png` | Top row: militia, phalanx, legion, knights, catapult. Below them: chariot, then old-art musketeers and cannon. |
| `05_babylon_units_3x.png` | Babylonian (green) legion stack, settlers/militia, knights and phalanx. |
| `06_babylon_militia_base_10x.png`, `07_babylon_knights_base_8x.png` | Close-ups of green bases: the player colour lands in square patches. |
| `08_roman_knights_8x.png` | Roman knights close-up: white barding merges into the white base. |
| `09_rome_and_ships_3x.png` | Rome (size 1, nothing inside), settlers, trireme, and sail. |
| `10_turn5_overview.png` | 3900 BC. A Roman unit is inside Rome, and the Babylonians have founded a city (bottom right). |
| `11_babylon_city_with_unit_5x.png` | The Babylonian city with its settlers standing in it. |
| `12_paris_overview.png`, `13_paris_town_5x.png`, `14_paris_town_2x.png` | Paris (French, blue) with nothing inside, the clearest view of the town. |

## Findings

### Units centred on their bases?
Yes. All nine painted units stand on the middle of their base disc in the game, as in the previews. Long weapons stick out past the base to the right: the militia's pitchfork, the phalanx's spear and the knights' lance. That matches the source art, but at 1x the pitchfork and spear read as a loose stick lying on the grass beside the figure.

Old-art units (musketeers, cannon) have no base. They are about twice as tall as the gouache foot soldiers, so the two styles look very different side by side (`04`).

### Bases and gear in the player colour?
Partly. This is the main problem.

- **The colour lands in 4x4 squares** (`05`, `06`, `07`). On Babylonian units only parts of each base turn green. The rest of the disc shows the rim's off-white, and the flags and shields are speckled. The green is also flat, with none of the gouache shading.
- **Cause.** When a region file loads, `GBitmap` sets each original pixel under a 4x4 HD block by majority vote: a key index if key pixels win, otherwise the nearest palette colour (`GBitmap.cs`, around line 1790). A key pixel then shows the colour of that original pixel. The bases are small and share most 4x4 blocks with the white rim or the figure, so many blocks lose the vote. Their key pixels then show rim white or figure brown. For example, the militia (`17_161_t3.png`) has 6 blocks that key wins and 16 mixed blocks it loses. The key colours in the PNGs are clean (exact `r == b, g == 0`; no near-magenta strays), so the fault is the 4x4 grid, not the paint.
- **White hides it.** The Romans are white, and the rim is white, so Roman units look evenly coloured (`04`). Every other civ shows the patches.
- **White speckle on gear.** On the knights and chariot, small keyed areas on the caparison and harness lose the vote one block at a time. They turn into white specks across the horse (`07`, `08`). With white Roman barding, the horse and the base blur into one pale shape (`08`).
- **Possible fixes** (not tried): make the key regions on the base cover whole 4x4 blocks (paint the inner disc as solid key with the rim outside those blocks); or change the loader so a block that holds any key pixels maps to the key index.

### Town with the white size number on its badge?
Yes. Rome (grey/white, `09`), Paris (blue, `13`) and the Babylonian city (green, `11`) all draw their badge filled with the player colour and a white "1". The red-roofed town shows around and below the badge.

Problems:

- **Magenta line on top of every badge.** Every city badge has a 20x2 px line of pure `#800080` (the primary key colour itself) along the top edge, just inside the black frame (`09`, `11`, `13`). In the source (`193_113_t4_k0-5.png`) those rows are the cyan secondary key. So an unresolved key colour reaches the screen there, probably in the `t4` overflow row above the cell. It is the only place an unresolved key colour reaches the screen; I counted pixels across three full screenshots.
- **The badge covers most of the town.** The badge covers the centre and top-left of the town. A unit standing in the city covers the lower half of the badge, including the number, and most of the rest of the town (`11`).
- The city name label ("Rome") runs over the trireme in the cell below (`09`). That is the usual label placement, not the art.

### Seams or missing art
- No seams in the unit or town sprites. The `t3` overflow on the tall units (legion's eagle, the catapult's and chariot's flags) joins cleanly.
- Huts (red shields) and special resources are still the old art, as expected.
- The other 19 unit types still use the old 4x art, as expected (the musketeers and cannon here).
- The painted terrain and coasts showed no tile seams in these views.

### Other things seen
- Some placed Roman units (knights, catapult, cannon) disappeared between turns 2 and 5. The Babylonians probably attacked them, but I didn't watch it.
- Bug: in my first game, after founding Rome I opened **CHANGE** on the city screen, pressed Escape to leave the production list, then Escape to leave the city screen. The game crashed with `IndexOutOfRangeException` in `GameEngine.F0_1ade_0421_SelectCityProduction` (called from `Overlay_20.F20_0000_0000` → `ProcessCityState`). The CHANGE list had offered only Barracks. Leaving the city screen with the EXIT button and without touching CHANGE worked.

## Notes on running

- **Build.** The .NET 8 SDK (8.0.1xx) fails to build: Avalonia 12.1.4's source generators need Roslyn 4.14, so all `x:Name` fields go missing (`CS9057`, then about 110 `CS1061` errors). With the .NET 10 SDK installed alongside, `dotnet build src -c Debug` succeeds with no changes and the program still runs on the .NET 8 runtime.
- **Stand-in data.** I generated it with `HD_SET=hd4 sh tools/standin/build.sh`. That needs the .NET SDK as well, for PicWriter.
- **Placed units stay hidden.** Units added to the save by editing the unit table stay invisible until they move. `MapManagement.F0_2aea_14e0_GetCellActiveUnitPlayerID` reads a presence layer from the map picture (pixel x + 160), and editing the `.SVE` does not set it.
- **Game windows.** The name dialogs ("Your name", "City name") are separate Avalonia windows, so I clicked them with the mouse. Pressing Return there did nothing.
