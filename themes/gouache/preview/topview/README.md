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
| `09_ingame_battle_frames.jpg` | A real battle in game: a Babylonian legion attacks the Roman catapult; dust bursts at the clash, and the catapult greys and fades. |
| `10_battle_frames_harness.png` | The same battle drawing, frame by frame (`TOPVIEW_BATTLE` in the harness): lunge, clash, fall back, smoke. |
| `11_city_screen_map_2x.png` | The city screen's small map: the painted hamlet and grassland shield under the worked-tile icons. |
| `12_ingame_big_cities.jpg`, `13_ingame_walled_rome_and_capital_3x.png` | Larger cities in game (sizes set in the save): Rome at size 9 with City Walls as the walled city with its domed palace, and Babylon at 13 as the marble capital, each in its clearing. |
| `14_ingame_walled_paris_4x.png` | Paris at size 6 with walls: the walled terracotta town. |
| `15_city_screen_size9_2x.png` | Rome's city screen at size 9: the walled town at the centre of its small map. The size badge is left out there, since the title gives the population and the worked-tile icons would cover it. |
| `16_after_city_screen.jpg` | Back on the map after closing the city screen, nothing left over from it. |
| `17_ingame_sizes_2_to_8.jpg` | In game, every step of the ladder between hamlet and city: Rome at 2, 3 (walled) and 7; Babylon at 4, 5 (walled) and 8 (walled). |
| `18_ingame_small_walled_towns.png` | Small walled towns in game (sizes 3 and 5): their own painted walls. |
| `22_walled_towns_sizes_3_to_14.png` | Walled towns from size 3 to 14 (harness): the painted walls at every step. |
| `23_reference_vs_game_check.png` | The aesthetic check: `compose.py` reference crops above, the game below (towns, units on bases, a fortified unit, a town name). |
| `24_painted_effects_check.png` | The drawn effects after repainting them as dabs: the fortified earth bank, a stack's second base, and battle smoke on land and sea. |
| `19_ingame_babylon_captured.jpg`, `20_ingame_captured_city_screen.jpg` | A Roman legion captures Babylon at size 3: the town keeps its picture and clearing and changes to a white Roman badge at size 2; its city screen shows the nearby Babylonian units in green. |
| `21_sea_battle_frames_harness.png` | A battle at sea: a red trireme attacks a blue sailing ship and loses, greying and fading under smoke on the water. |

## Also fixed

- **Key colours.** Any key pixel in a 4x4 block now makes the block's original pixel the key's index, so small bases take their player colour whole and no longer come out in square patches. This matters where the classic sprites still show, such as the city screen.
- **City production list.** A refactor in a3008f3 had inverted the unit filter, so a new city offered only Barracks. It offers units again, with ships only in coastal cities.
- **Escape crash.** Closing the production list with Escape crashed the game; now the city keeps what it was building.
- **`compose.py` with Pillow 12.** It runs again; a numpy value passed as a blur radius had broken it.

## Battles and the city screen

- **Battles** are painted too: the attacker lunges and falls back, dust and sparks burst where they meet, and the loser greys, sinks and fades under rising smoke. No art files are needed, so they work for any theme with this view.
- **The city screen's small map** gets the same painted town, foreign units, huts, resources and shields, drawn through a second overlay (`GBitmap.Inset`). The worked-tile icons stay on top, so the size badge is left out there; the title gives the population. Walled towns use the ladder's `_w` pictures, there as on the map.

## Review, element by element

Visual changes go through the `aesthetic-check` skill (`.claude/skills/aesthetic-check/SKILL.md`): side-by-side crops against the reference renders, at game size and enlarged, in the real game.

Each element on the map, judged against `compose.py`'s renders and checked in the running game unless noted:

| Element | Verdict |
| --- | --- |
| Painted ground, coasts, rivers, moving water | Unchanged painted map; shows between all objects. |
| Towns, sizes 1 to 13 | Right ladder picture for each size, in a clearing scaled to the town (`17`, `13`). |
| Walled towns | Walled pictures at every step. The walls run round each picture's very edge, which `compose.py`'s edge softening eats away on small towns, so a walled picture's outline is softened only lightly and its edge band stays solid: the painted walls, towers and gates show from size 2 up (`18`, `22`). On the smallest walled town (sizes 2 and 3) the painted wall is a low thin one. |
| Size badge and name | Same size, place and type as the renders, in each player's colour; white Roman badges read on all grounds. |
| The nine gouache units | Same scale, anchors and bases as the renders, in all eight player colours. |
| The other 19 units | Default painted set, toned and sized by kind; they sit on bases like the gouache units (`02`). Their painting is a little crisper than gouache, the one remaining style difference, until gouache versions are painted. |
| Huts, resources, grassland shield | Painted objects, toned; the shield is small and quiet. |
| Unit states (active, sentry, fortified, stack, orders) | Painted forms: the fortified bank and battle sparks and smoke are laid on as soft dabs of paint with light from the top left, not flat strokes (`24`); a stack's second base peeks out behind. Checked in game for active and fortified. |
| Scrolling | While the ground is redrawn, the painted objects are hidden for a moment instead of standing on the old cells. |
| Town names | Written after the units, so a unit in a town never covers its name. |
| Moving units | Slide smoothly between cells. |
| Battles, land and sea | Painted lunge, dust, smoke and fade (`09` in game; `10`, `21` frame by frame). A sea battle has not been caught in game: ships placed by editing a save stay hidden until they move, and the AI ships did not attack in the turns recorded. |
| City capture | Town, clearing and badge change hands cleanly (`19`). |
| City screen's small map | Painted town and foreign units under the worked-tile icons; no badge there (`15`, `20`). |
| Dialogs and menus | Drawn over the map as before; the painted map comes back clean after each. |

Still classic, outside the map: the interface panels, fonts and full-screen pictures (the stand-in data has placeholders for those).

## Reproducing

```bash
TOPVIEW_PREVIEW=out.png TOPVIEW_THEME=themes/gouache/hd TOPVIEW_FALLBACK=assets TOPVIEW_ENGINE=themes/gouache/preview/engine-v8.png dotnet run --project tests/HDLayerTests -c Release
```

Add `TOPVIEW_BATTLE=battle.png` for the battle frames (it also writes `battle-sea.png`). The in-game shots come from a Debug build run under Xvfb with the stand-in data (`HD_SET=hd4 tools/standin/build.sh`) and `themes/gouache/hd` copied to `~/Dos/Civ1/themes/gouache`. The preferences were `{"Theme": "gouache", "Debug": {"DebugMode": true}}`, and extra units were placed in the save with `../ingame/place_units.py` from the `gouache-ingame-shots` branch.
