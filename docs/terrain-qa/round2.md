# Terrain QA, round 2: Simon's decisions applied

Full numbers: `scorecard.md`. Pictures: `pack/`. For the question about the alternative generator, the s01 classic-vs-smoothed views come first: `0-s01-world.jpg` and `0-s01-flat.png`. Then `*-closeups.jpg`: four fixed close-ups per world (polar edge, temperate start, desert/jungle border, river), classic left and smoothed right, at the game's own zoom.

## Decisions applied
1. **Default worlds are pass/fail.** The 10 default worlds are pass/fail for coherence, relief, rivers, balance and playability. The 8 extreme-settings worlds are in their own tables: climate (B) is pass/fail there, everything else is report only. Regression and determinism (G) run on all 18.
2. **Balance ignores the arctic change.** Terrain share, food, shields and trade are measured on non-arctic land. The arctic difference is a separate report-only line (E0): 5.5 tiles in the median default world, 25 in the worst.
3. **Polar specks cleaned up.** After the polar-cap stage, a lone tundra tile on the polar rows becomes ice. That is land for land, so coastline, continents and rivers are unchanged (E1–E3 and E7 stay at 0).

## Headline
- **Default worlds:** smoothed fails 20 of 44 checks; classic fails 28 of 31; youngest age fails 30 of 31.
- **Extreme-settings worlds, climate checks:** smoothed fails 1 of 4 (B4, latitude ordering).

## What passes now (smoothed, default worlds, worst world)
- **Land in fragments of 1–2 tiles:** 8.7% (target ≤ 10).
- **Median region size:** forest 5, mountains 4, arctic 4, jungle 7.
- **Climate (B1–B3):** all 0.
- **Mountains:** 96% in ranges (median world 100%), and 80% of ranges have foothills.
- **Rivers:** every river reaches the sea, and every source sits beside high ground.
- **Balance:** total food changes by 1.9%; trade, resources, huts and continents are unchanged.
- **Playability:** start quality (F1) and viable city sites (F3) pass.
- **Regression (G1):** with smoothing off, all classic and youngest-age worlds are byte-identical to the baseline.
- **Determinism (G2):** a second full run is identical, smoothed worlds included.

## Still failing, graded
**Blocker:** none.

**Major:**
- **Single-tile regions (A2)** are still above 20% for most terrains on the worst default world. Medians are around 0–30%. The worst offenders are hills (the foothills placed as single tiles by design) and tundra (median region 2).
- **Median region size (A1)** is below 4 for desert, plains, grassland, hills, tundra and swamp on the worst world.
- **E4:** largest change in a non-arctic terrain share, 4.0 points worst (median 2.1).
- **E5:** shields change by 4.8% worst (median 2.3%).
- **F2:** start fairness. The best-to-worst start spread is 8 points wider than classic on the worst world (median 1 point narrower).
- **B4 latitude ordering:** one or two steps out of order (see the views).
- **D3:** river junctions with more than three channels. This is classic and on hold per Simon.

**Minor:** none new.

## Not done, and why
- **Render checks (section 3):** not built yet. Next.
- **Old-save load test (G3):** verified by reading the code, not by running it. A save stores its map in a .MAP file, and loading reads that file. The generator, and so smoothing, only runs when a new game starts. Driving the save through the in-game menus in the test display was unreliable, so a real save/load run is still owed.
- **Play test with smoothing on (section 5):** next, after the render checks.
