# OpenCivOne art exploration

This folder is a **visual exploration of themes** for OpenCivOne. It is a moodboard and mockup archive, not shipped game art.

Nothing here is wired into the engine, the HD sprite set, or the painted map. Treat it as reference for a possible look, not as assets the game loads.

## Direction that stuck

After plates in many traditions (Baroque oil, Mughal miniature, Art Deco, glazed brick, Mesoamerican codex, Palekh, Egyptian, Shan shui, mural, and several cartographic treatments), **ukiyo-e** is the direction that stuck.

The working picture of that look:

- The map is **one continuous painted ground**, not a board of terrain tiles.
- The **grid is invisible**. Units and cities still sit on cells, but the painting does not draw cell edges.
- **One city or unit per cell.** Stacks are not a pile of figures.
- Units read as **flat print tokens**, with an optional cream outline so they separate from the ground.
- Stack size is a **count seal** (a small numbered stamp), not extra bodies.

## How the folders map to rounds

Browse the **curated set** at the top of this folder first. `iterations/` holds earlier takes and working plates.

| Folder | Round | What you are looking at |
| --- | --- | --- |
| `01-baroque.png` … `10-mural.png`, `moodboard.jpg` | Style plates | One scene (city + rider on grass) in ten painted traditions. Ukiyo-e is `02-ukiyoe.png`. |
| `graphic/`, `graphic-board.jpg` | Graphic / cartographic screens | Same game-screen layout treated as pixel, blueprint, sketch, topo, portolan, hydro, atlas, pictorial map. |
| `screens/`, `screen-board.jpg` | Screen mockups | Those cartographic ideas as a Civilization-like sidebar + map. |
| `painted-screens/`, `painted-screen-board.jpg` | Painted styles on a screen | The ten traditions applied to a full game screen. |
| `painted-variations/` | Painted variations | Hokusai vs Hiroshige ukiyo-e, plus classicism and Baroque screens. |
| `topdown/`, `topdown.jpg` | Top-down correction | Straight-down camera so the map reads as an atlas, not a landscape with a horizon. |
| `boards/`, `boards.jpg` | Comparison boards | Side-by-side plates from the early rounds. |
| `blended/` | Seamless ground | Selected ukiyo-e / classicism / Baroque maps with continuous grass, forest, hills, and sea. |
| `composite/` | Map + pieces | Ground, cities, and units composited together. `*-grid.jpg` draws the hidden cell grid on top for checking placement. |
| `ukiyo-units/` | Unit options | Print token (A), board-game token (B), and ink (C), plus size comparisons. |
| `ukiyo-states/` | Unit states | Idle / moved / stacked looks, cream outline variants, merged unit sheets, count seals. |
| `ukiyo-factions/` | Factions | Roman-ancient, modern, and Asian-medieval pieces on the **same** map style. |
| `ukiyo-v2/` | v2 improvements | Larger cities, farmland and mines painted into the ground, thinner railroad, Roman / modern / Asian-medieval scenes, before/after sheets. |
| `ukiyo-v3/` | v3: blending | Edits of the v2 screens: cities with no plinth grown out of the land (`roman-a`), walled-town vignettes in cleared earth (`roman-b`), modern sprawl (`modern-c`); farmland as curving or hedged fields that follow the land instead of a checkerboard; units about 15% smaller. `water-test.gif` animates the sea and rivers of `roman-b` (`scripts/water_test.py`). |
| `ukiyo-v4/` | v4: concept sheets for every asset type (`board.jpg` shows all twelve) | Cities by era and size painted into the ground; terrain improvements (fields, mines, roads, rails, fortress, pollution, huts); every terrain in one continuous map; all 28 units as small tokens; the interface (main screen, menu, dialog); the city screen; Encyclopedia scenes; the 14 leaders; advisors, ancient and modern; palace levels and the city view; full-screen pictures; animation storyboards (combat, celebration, founding a city). Made from the v2 and v3 screens as references (`scripts/concept_v4.py`, prompts beside the images). |
| `iterations/` | Working takes | Distinct generations and process sheets; see below. |
| `scripts/` | Generator scripts | How the plates were requested. They read `OPENAI_API_KEY` from the environment and do not contain a key. |

## Conventions learned

These are the notes that kept coming back while iterating. They are design rules for this look, not engine requirements.

- **No landscape-with-grid.** A horizon, vanishing point, or “painting of a countryside with a chessboard on it” fights the game. The camera is straight down.
- **No chessboard tiles.** Grass must flow across where a cell edge would be. No grout, no rectangular texture patches, no graph paper.
- **Cities must dominate units.** A keep that is the same size as a horse disappears. Cities got larger in v2.
- **Improvements are painted into the ground** (fields, paddies, mines, roads), not stamped on as separate tiles sitting on top of a checkerboard.
- **Railroad is thin.** A fat rail band reads as a wall and breaks the print.
- **Fields follow the land.** Square checkerboard plots look stamped on; curving or hedged fields along rivers and contours blend in (v3).
- **No plinths under cities.** A city fades into cleared earth, lanes and outlying houses instead of standing on a raised base (v3).
- **Factions share the map style.** Roman, modern, and Asian pieces change costume and architecture, not the ground language.

## Iterations

`iterations/` is the working pile. Curated finals above stay easier to browse because this folder is separate.

| Subfolder | Contents |
| --- | --- |
| `topdown-r2/` … `topdown-r4/` | Full-screen top-down rounds before the set in `topdown/`. |
| `blend-takes/` | Letter-suffixed full generations (`ukiyo-e-a.png` …) plus the prompts that made them, and small `full-` / `side-` sheets. |
| `composite-raw/` | Raw generated plates for ground, cities, units, resources, portraits. |
| `composite-work/` | Cut-out / composited versions of those plates, plus labeled sheets (`sheet-A.jpg`, `*-plategrid.jpg`). |
| `v2-process/` | Before/after crops and per-faction scene takes that fed `ukiyo-v2/`. |

Left out of the archive on purpose: zoom-debug crops (`z-*`, `zoom*`), grid overlay proofs of takes already present as full frames, tight cell crops, ghost/mask overlays, JPEG copies of a PNG that is already here, and `final-*.jpg` files that are byte-identical to `blended/`.

## Not in-game assets

OpenCivOne still draws the map from `assets/hd`, `assets/hd4`, and `assets/painted` as described in [hd-art.md](../hd-art.md). This exploration does not replace those folders. If a ukiyo-e set is ever built for the game, it would be a separate art pipeline, not a drop-in of these mockups.
