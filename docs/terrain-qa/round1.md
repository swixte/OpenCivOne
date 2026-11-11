# Terrain QA, round 1: baseline, youngest age, opt-in smoothing

Full numbers: `scorecard.md`. Pictures: `pack/` (classic and smoothed side by side for worlds s01, s05 and climate-high, as an isometric world view and a flat whole map).

## Summary

| | Checks failed (of 31 data checks, plus 10 balance checks) |
|---|---|
| Classic generator (baseline) | 29 of 31 |
| Youngest age (no code change) | 30 of 31 |
| Smoothing on | 23 of 31, and 3 of 10 balance checks |

- **Youngest age** barely helps. Land in fragments of 1–2 tiles drops from 46% to 38% (median world), but mountains in ranges get worse. It isn't a route to coherent terrain.
- **Smoothing on**, median world:
  - region sizes go from 1 tile to 4–14 tiles;
  - land in fragments of 1–2 tiles drops from 46% to 10%;
  - mountains in ranges of 3 or more go from 48% to 100%;
  - every climate check passes (no arctic away from the poles, no desert or jungle at high latitude, no forbidden neighbours);
  - every range has foothills, and every river source sits beside hills or mountains.
- **Untouched by smoothing on every world:** land, coastline, river tiles, continents, special resources and huts.

## Regression and determinism (section G)
- **Smoothing off:** all 36 classic and youngest-age worlds are byte-identical to the baseline.
- **Same seed and settings twice:** identical maps.
- **Not yet tested:** loading a save made before the change.

## Failures, graded

**Blocker:** none. The classic game is unchanged, and nothing crashed.

**Major:**
1. **Coherence (A1/A2/A3) fails on the worst world** for most terrains, although the median world mostly passes. The worst cases are:
   - the extreme-settings worlds, where a terrain has only a handful of tiles;
   - tundra, which the generator's polar-cap stage scatters as single tiles on the ice. That stage runs after smoothing and after continents are worked out.
2. **Balance (E4/E5)** misses by a few points. The largest terrain-share change is 4.5 points (median 2.8); food changes by 6.1% and shields by 6.0% (medians 2.9% and 2.4%). Most of it is a real conflict between two targets: clearing arctic away from the poles (B1) removes about 37 arctic tiles, about 3.4 points of land. Arctic can only stand at the poles, so that share can't be restored.
3. **River junctions joining more than three channels (D3):** 2 in the worst world, classic and smoothed alike. The generator's river stage makes them (a comment in the code says that stage doesn't yet work like the original's). Smoothing may not change rivers (E3), so it can't fix them.
4. **Latitude ordering (B4)** is out of order by 1–2 steps, because the terrain mix is kept and zones can't be rearranged.

**Minor:**
- Hills are still often single tiles (median 3 per region): the foothills placed beside ranges and river sources are single hill tiles by design.

## Decisions for Simon
1. **Thresholds on the worst world.** Should they apply to the extreme-settings worlds, or only to the 10 default worlds? On the default worlds alone, most coherence checks pass.
2. **Arctic conflict.** Accept the arctic share change that the climate rule causes (B1 against E4), or keep mid-latitude arctic?
3. **Polar specks.** Allow a light clean-up of single tundra tiles on the polar rows, swapping land for land only, after the polar-cap stage?
4. **The alternative**, per the working rules: keep coastlines, rivers and exact terrain counts, but re-place land terrain from smooth elevation and moisture fields plus latitude. Estimate about a day:
   - fields from noise seeded by the world's seed;
   - terrain assigned by ranking cells and filling each type's exact count;
   - mountains along elevation ridges, hills on their flanks;
   - swamp and jungle from moisture and latitude.

   It would meet the coherence and balance targets together, since exact counts are preserved by construction, at the cost of looking less like Civ 1's own maps.

## Not done yet this round
- Section F (playability: start fairness, viable city sites).
- Section 3 (render checks).
- Section 5 (play test with smoothing on).
- The four fixed close-ups for the image pack.
