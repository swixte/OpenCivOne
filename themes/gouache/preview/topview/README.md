# Gouache towns and units in the real game

The game now draws the gouache theme's towns and units the way `compose.py`'s map renders do. Before, each unit was squeezed into a 16x16 classic sprite and every town was the same sprite under a big badge (see `../ingame` on the `gouache-ingame-shots` branch).

`src/Graphics/TopView.cs` draws the towns, units, huts and resources top-down over the painted map. It follows `compose.py`'s rules:

- **Towns:** picked from the town ladder by size (`soft_town` processing), each standing in a clearing the painted ground opens round it.
- **Badges and names:** a 13 px size badge in the player colour on the town's top-left corner, and the name in bold serif with a dark shadow.
- **Units:** all at the one shared scale, anchored at the feet or hull, on bases sized and squashed by type.
- **Player colour:** the painting's blue parts take the player's colour.

Gameplay marks have painted forms:

- the active unit's base is ringed while it blinks;
- a sentried unit is grey;
- a fortified unit has an earthwork ring;
- a stack shows a second base behind;
- a unit with an order in progress shows its letter on a paper tag.

The gouache set has nine units and no huts or resources of its own. The other 19 units, the huts and the resources come from the default painted set, toned brighter and sized to the gouache unit of their kind, so the musketeers stand as tall as a phalanx. The grassland shield is drawn smaller and quieter.

| File | Shows |
| --- | --- |
| `01_compose_vs_game_renderer.jpg` | Top: `compose.py`'s render. Bottom: the game's renderer over the same ground, with a hut, a resource, musketeers and a cannon added. |
| `02_ingame_units_and_rome.jpg`, `03_ingame_units_2x.png` | The real game, turn 2: Roman units on white bases, Babylonian ones on green, both ships, and Rome as a hamlet with its badge and name. |
| `04_ingame_babylon_city.jpg`, `05_ingame_babylon_3x.png` | A Babylonian city, and a fortified Babylonian unit in its earthwork ring. |
| `06_ingame_paris.jpg` | Paris, in blue, on the French island. |
| `07_ingame_rome_clearing_2x.png` | Rome in the clearing the painted ground opens round it. |
| `08_after_change_escape_no_crash.jpg` | Back on the map after CHANGE, Escape, Escape, the sequence that used to crash. |

## Also fixed

- **Key colours.** Any key pixel in a 4x4 block now makes the block's original pixel the key's index, so small bases take their player colour whole and no longer come out in square patches. This matters where the classic sprites still show, such as the city screen.
- **City production list.** A refactor in a3008f3 had inverted the unit filter, so a new city offered only Barracks. It offers units again, with ships only in coastal cities.
- **Escape crash.** Closing the production list with Escape crashed the game; now the city keeps what it was building.
- **`compose.py` with Pillow 12.** It runs again; a numpy value passed as a blur radius had broken it.

## Not changed

- The city screen's small map keeps the classic sprites.
- Battles play the classic animation.
- The gouache theme has no walls art of its own, so walled towns use the ladder's `_w` pictures.

## Reproducing

```bash
TOPVIEW_PREVIEW=out.png TOPVIEW_THEME=themes/gouache/hd TOPVIEW_FALLBACK=assets TOPVIEW_ENGINE=themes/gouache/preview/engine-v8.png dotnet run --project tests/HDLayerTests -c Release
```

The in-game shots come from a Debug build run under Xvfb with the stand-in data (`HD_SET=hd4 tools/standin/build.sh`) and `themes/gouache/hd` copied to `~/Dos/Civ1/themes/gouache`. The preferences were `{"Theme": "gouache", "Debug": {"DebugMode": true}}`, and extra units were placed in the save with `../ingame/place_units.py` from the `gouache-ingame-shots` branch.
