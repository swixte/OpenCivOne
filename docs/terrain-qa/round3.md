# Terrain QA, round 3: the field-based re-layout

## Status for review
- **Ready to judge:** the terrain layout. That covers the scorecard (`scorecard.md`), the render checks (`render-checks.tsv`) and the pictures in `pack/`, s01 first.
- **The pictures in `pack/`** were rendered before the open-land detail (meadow patches, small copses, low rises) was added; `pack/open-land-before-after.jpg` shows that change on its own.
- **Not finished:**
  - The section 5 play test (300 turns with the re-layout on) is still running. The first two runs found three crashes in the original port, unrelated to the terrain; all three are fixed.
  - The real save-and-reload test (G3) is still owed. Section G below explains why.

Simon's working rule said: if majority-filter smoothing can't reach the targets, build the field-based alternative. The user said go. The smoothing switch (`TerrainSmoothing` in preferences, still off by default) now runs `TerrainRelayout` (`src/Game/TerrainRelayout.cs`) in place of the majority filter. The lone-tundra clean-up on the polar rows stays.

## How it works
1. **What stays exactly as generated:** coastlines, continents, lakes and rivers. The outermost rows (the polar caps) are also left alone.
2. **Counts:** every land terrain keeps its exact number of tiles. Arctic stays only within three rows of a pole; its other tiles are shared out among the other terrains in proportion to their counts.
3. **Three smooth fields,** wrapping around the world and seeded from the map, so the same map always gives the same result:
   - **elevation:** ridged noise, rising inland, lifted around river sources so rivers rise in high ground;
   - **moisture:** broad wet and dry regions with smaller variation inside them;
   - **temperature:** by latitude, cooler with height.
4. **Placement:** each terrain takes the free cells that suit it best, in this order:
   - arctic: coldest polar cells;
   - mountains: highest;
   - hills: high, beside mountains;
   - tundra: coldest;
   - desert: hot and dry, tropics only;
   - jungle: hot and wet, tropics only;
   - swamp: wet lowland;
   - forest: wet, leaning poleward;
   - grassland: the wetter of what remains;
   - plains: the rest.
5. **Clean-up:** count-preserving swaps gather stray tiles into regions and join tiles that share no edge with their own kind. No swap may break the latitude limits or the rules on neighbours.

## Headline
| | Default worlds |
|---|---|
| Classic | 28 of 31 failed |
| Field re-layout | 12 of 44 failed (the majority filter in round 2: 20 of 44) |

On the extreme-settings worlds, every climate check now passes: 0 of 4 failed.

## Passing now (default worlds, worst world)
- **Fragments:** land in regions of 1–2 tiles is 5.0% worst, 3.3% median.
- **Climate (B1–B4):** all pass, **including the pole-to-equator ordering of the climate zones**, which failed in rounds 1 and 2.
- **Relief:** 88% of mountains in ranges of 3 or more (median 92%), and ranges with foothills 62%.
- **Rivers:** every river reaches the sea or a lake, and river sources sit on high ground (67% worst, 79% median).
- **Balance:** every check passes. Terrain shares and average yields stay within targets; land, coastline, rivers, continents, resources and huts are unchanged.
- **Playability:** start quality (F1) and viable city sites (F3) pass.
- **Regression and determinism (G1, G2):** with the switch off, classic worlds are byte-identical to the baseline, and two full runs give identical worlds.

## Still failing
- **Region sizes on the worst world (A1/A2):** desert, forest, hills, mountains, tundra, swamp and jungle. The medians all pass or are close; these fail only on the one or two worlds where a terrain has very few tiles.
- **D3 river junctions:** classic behaviour, on hold per Simon.
- **F2 start fairness:** the best-to-worst start spread is 19 points wider than classic on one world, but 1.5 points in the median world. Coherent land makes rich valleys richer; evening out starts would mean the game's own start placement, which is out of scope.

## Measurement change, for Simon to confirm
E4/E5 (balance) are now measured as **shares of, and average yields per tile on, each world's non-arctic land, rivers excluded**. This is my reading of "measure on non-arctic land": it ignores the chosen arctic change. Rivers are excluded because they never change (E3), and their fixed trade would otherwise only measure the moved arctic.

## Pictures
`pack/`: the `0-s01-*` files come first. Each world has a world view, a flat whole map and four close-ups, classic on the left and re-laid on the right.

## Render checks (section 3)
Measured on worlds s01, s05 and s09, classic and re-laid, rendered at the game's zoom over the whole map (`ISO_RENDERCHECKS`, results in `render-checks.tsv`):
- **Neighbouring cells of one range, hill group or wood drawn in different art variants:** 0 in every world. Target 0, pass.
- **Neighbouring same-terrain cells drawing the identical sprite:** 2.3–4.1%. Target 15% or less, pass.
- **Sprites larger than a city, mountains apart:** none. Target none, pass.
- **Overlay visibility on the legend sheet:** not automated yet. The legend is checked by eye.
