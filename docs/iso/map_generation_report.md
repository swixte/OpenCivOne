# Map generation diagnosis (for Simon)

The "wild" look comes from the terrain data, not the art. Measured on three worlds the game itself generated.

## a. The generator
A stage-by-stage port of Civilization 1's own (`src/Game/CodeObjects/MapInitAndIntro.cs`):
- Continents grown from random-walk "clouds".
- Temperature by latitude.
- Climate (wetness) passes.
- An "age" pass of 1,600–4,000 random-walk steps that cycles single tiles: grassland → forest → jungle → swamp → grassland; plains and tundra → hills → mountains; desert → plains.
- Rivers.
- Polar caps.

It is not simplified. One known difference: a comment in the code says the river stage doesn't yet work like the original's.

## b. Region statistics
Regions are 4-connected. Single-tile percentages are given for worlds A / B / C.

- **Median region size:** 1 tile for every land terrain, in all three worlds.
- **Single-tile regions:**

  | Terrain | Single-tile regions |
  |---|---|
  | Mountains | 82 / 66 / 76% |
  | Hills | 75 / 66 / 63% |
  | Forest | 73 / 78 / 69% |
  | Jungle | 78 / 71 / 88% |
  | Swamp | 100 / 93 / 95% |
  | Desert | 57 / 67 / 77% |
  | Grassland | about 58–60% |
  | Plains | 55–63% |

- **Mean region size:** 1.0–2.8 tiles. Arctic is the exception (median 2), and rivers average 7 tiles per system.
- **Tiles out of climate (A / B / C):**
  - arctic away from the two polar rows: 30 / 43 / 20
  - desert or jungle beyond 60% latitude: 5 / 14 / 2
  - jungle touching desert or arctic: 6 / 12 / 6
- **Mid-latitude arctic** (rows 5–11 and 41–44) comes from the generator itself, so the white slabs next to palms are real map data.
- **Cause:** the random-walk age pass flips one tile at a time, which is what fragments everything. The 16-pixel icons hid this; realistic art exposes it.

## c. World settings
Every test game used "Start a new game", which fixes land mass, temperature, climate and age all at 1: normal land, temperate, normal climate, 4 billion years.

## Optional terrain smoothing pass (costed, not built)
Opt-in and off by default. It would run after the classic generator and before map groups, city sites, resources and huts are worked out, so the game uses the smoothed map consistently.

1. **Majority filter**, 2–3 passes: a land tile whose type appears in at most one of its eight neighbours takes the most common neighbouring type. Water and rivers are never changed, so coastlines, continents and rivers stay exactly as generated.
2. **Latitude limits:**
   - arctic only within 3 rows of a pole, otherwise tundra
   - jungle and desert only within about 60% of the equator, otherwise grassland or plains
   - no jungle touching desert or arctic
3. **Mountains:** single mountain tiles become hills unless another mountain is within two tiles. This gathers ranges without moving any.
4. **Balance:** each terrain type is topped back up to its original count by converting edge tiles of neighbouring regions, so proportions stay within about 2% and food and shield balance is unchanged.

**Estimate:** about half a day, including a preference switch, a test, and before/after renders of the same world (`ISO_DUMP` + `ISO_WORLD`). It changes the maps the game makes, so it's Simon's call.

Statistics tool: `tools/isoproof/terrain_stats.py` (run it on worlds dumped with `ISO_DUMP`).
