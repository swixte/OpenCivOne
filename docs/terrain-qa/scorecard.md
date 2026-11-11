# Terrain QA scorecard

18 fixture worlds (tools/isoproof/terrain-qa/fixtures.json), modes: classic, young, smoothed, commit a0e8ead + field re-layout.

Default worlds (pass/fail): classic 28 of 31 failed, young 30 of 31 failed, smoothed 12 of 44 failed. Extreme-settings worlds (climate checks only): classic 4 of 4 failed, young 4 of 4 failed, smoothed 0 of 4 failed; their other checks are report only.

## Default worlds (10): data checks

Each cell: worst case across the worlds / median, then PASS or FAIL on the worst case.

| Check | classic | young | smoothed | Target |
|---|---|---|---|---|
| A1 median region size: Desert | 1 / 1 FAIL | 1 / 1 FAIL | 3 / 6 FAIL | >= 4 |
| A2 single-tile regions: Desert (%) | 85.7 / 64.6 FAIL | 77.8 / 64.9 FAIL | 40 / 0 FAIL | <= 20 |
| A1 median region size: Plains | 1 / 1 FAIL | 1 / 1 FAIL | 8 / 10 PASS | >= 4 |
| A2 single-tile regions: Plains (%) | 68.4 / 56.8 FAIL | 59.3 / 52.4 FAIL | 16.7 / 0.0 PASS | <= 20 |
| A1 median region size: Grassland | 1 / 1 FAIL | 1 / 1 FAIL | 6 / 10 PASS | >= 4 |
| A2 single-tile regions: Grassland (%) | 69.1 / 66.1 FAIL | 69.6 / 59.6 FAIL | 20 / 6 PASS | <= 20 |
| A1 median region size: Forest | 1 / 1 FAIL | 1 / 1 FAIL | 6 / 8 PASS | >= 4 |
| A2 single-tile regions: Forest (%) | 80.7 / 74.1 FAIL | 81.4 / 72.0 FAIL | 37.5 / 8.7 FAIL | <= 20 |
| A1 median region size: Hills | 1 / 1 FAIL | 1 / 1 FAIL | 5 / 7 PASS | >= 4 |
| A2 single-tile regions: Hills (%) | 81.4 / 75.2 FAIL | 85.4 / 77.0 FAIL | 23.1 / 11.7 FAIL | <= 20 |
| A1 median region size: Mountains | 1 / 1 FAIL | 1 / 1 FAIL | 2 / 4 FAIL | >= 3 |
| A2 single-tile regions: Mountains (%) | 89.7 / 81.0 FAIL | 90 / 83 FAIL | 33.3 / 15.5 FAIL | <= 20 |
| A1 median region size: Tundra | 1 / 1 FAIL | 1 / 1 FAIL | 3 / 3 FAIL | >= 4 |
| A2 single-tile regions: Tundra (%) | 82.8 / 75.6 FAIL | 76.7 / 74.0 FAIL | 19.0 / 7.0 PASS | <= 20 |
| A1 median region size: Arctic | 2 / 2 FAIL | 2 / 2 FAIL | 4.5 / 6.0 PASS | >= 4 |
| A2 single-tile regions: Arctic (%) | 45.9 / 38.2 FAIL | 41.7 / 36.1 FAIL | 6.7 / 0.0 PASS | <= 20 |
| A1 median region size: Swamp | 1 / 1 FAIL | 1 / 1 FAIL | 2 / 6 PASS | >= 2 |
| A2 single-tile regions: Swamp (%) | 96.7 / 90.8 FAIL | 95.8 / 90.1 FAIL | 33.3 / 7.1 FAIL | <= 20 |
| A1 median region size: Jungle | 1 / 1 FAIL | 1 / 1 FAIL | 3 / 6 FAIL | >= 4 |
| A2 single-tile regions: Jungle (%) | 88.6 / 82.7 FAIL | 91.4 / 80.9 FAIL | 44.4 / 17.4 FAIL | <= 20 |
| A3 land tiles in regions of 1-2 tiles (%) | 52.0 / 47.0 FAIL | 43.2 / 39.9 FAIL | 5.0 / 3.3 PASS | <= 10 |
| B1 arctic tiles more than 3 rows from a pole | 43 / 28 FAIL | 48 / 31 FAIL | 0 / 0 PASS | 0 |
| B2 desert or jungle beyond 60% latitude | 7 / 6 FAIL | 15 / 4 FAIL | 0 / 0 PASS | 0 |
| B3 forbidden neighbours (jungle by desert/arctic, desert by arctic/swamp) | 14 / 7 FAIL | 13 / 7 FAIL | 0 / 0 PASS | 0 |
| B4 latitude ordering arctic > tundra > forest > grass/plains > desert/jungle (steps out of order) | 3 / 2 FAIL | 3 / 2 FAIL | 0 / 0 PASS | 0 |
| C1 mountain tiles in ranges of 3 or more (%) | 12.8 / 41.9 FAIL | 0 / 35 FAIL | 87.5 / 92.4 PASS | >= 70 |
| C2 mountain ranges with hills beside them (%) | 54.3 / 69.2 FAIL | 29.4 / 62.6 FAIL | 62.5 / 71.4 PASS | >= 60 |
| D1 river systems reaching the sea or a lake (%) | 100 / 100 PASS | 83.3 / 100.0 FAIL | 100 / 100 PASS | 100 |
| D2 river sources beside hills or mountains (%) | 55.6 / 81.8 PASS | 44.4 / 82.9 FAIL | 66.7 / 78.9 PASS | >= 50 |
| D3 junctions joining more than three channels | 2 / 0 FAIL | 2 / 0 FAIL | 2 / 0 FAIL | 0 |
| D4 river tiles within 3 rows of a pole | 0 / 0 PASS | 0 / 0 PASS | 0 / 0 PASS | 0 |

## Default worlds: E. balance, smoothed against classic

Terrain shares and the average food, shields and trade per tile are measured over each world's non-arctic land, rivers apart (they never change, E3); the arctic difference is on its own line.

| Check | worst / median | Target |
|---|---|---|
| E1 land tiles changed (count) | 0.0 / 0.0 PASS | 0 |
| E2 coastline tiles changed (count) | 0.0 / 0.0 PASS | 0 |
| E3 river tiles changed (count) | 0.0 / 0.0 PASS | 0 |
| E0 arctic tiles changed (count, report only) | 43.0 / 28.0 | report only |
| E4 largest change in a terrain share of non-arctic land (points) | 0.4 / 0.3 PASS | <= 2 |
| E5 average food per non-arctic land tile changed (%) | 0.1 / 0.1 PASS | <= 3 |
| E5 average shields per non-arctic land tile changed (%) | 0.6 / 0.4 PASS | <= 3 |
| E5 average trade per non-arctic land tile changed (%) | 0.0 / 0.0 PASS | <= 3 |
| E6 special resources changed (%) | 0.0 / 0.0 PASS | <= 10 |
| E6 huts changed (%) | 0.0 / 0.0 PASS | <= 10 |
| E7 continents changed (count) | 0.0 / 0.0 PASS | 0 |

## Default worlds: F. playability, smoothed against classic

Starts are the 7 best city sites at least 10 cells apart, scored by the food (x3), shields (x2) and trade of their 21-cell working area; a viable site scores 60 or more.

| Check | worst / median | Target |
|---|---|---|
| F1 starts below the classic median start (count) | 1.0 / 0.0 PASS | <= 3 of 7 |
| F2 start spread best-worst, smoothed minus classic | 19.0 / 1.5 FAIL | <= 0 |
| F3 viable city sites changed (%) | 5.3 / 3.1 PASS | <= 10 |

## Extreme-settings worlds (8): data checks

Each cell: worst case across the worlds / median; climate checks (B) are PASS/FAIL, the rest report only.

| Check | classic | young | smoothed | Target |
|---|---|---|---|---|
| A1 median region size: Desert | 1 / 1 | 1 / 1 | 2 / 15 | >= 4 |
| A2 single-tile regions: Desert (%) | 100 / 71 | 85 / 73 | 37.5 / 0.0 | <= 20 |
| A1 median region size: Plains | 1 / 1 | 1 / 1 | 5 / 12 | >= 4 |
| A2 single-tile regions: Plains (%) | 58.2 / 56.0 | 60 / 52 | 25 / 4 | <= 20 |
| A1 median region size: Grassland | 1 / 1 | 1 / 1 | 5.5 / 12.2 | >= 4 |
| A2 single-tile regions: Grassland (%) | 71.3 / 66.1 | 69.5 / 60.9 | 31.2 / 2.9 | <= 20 |
| A1 median region size: Forest | 1 / 1 | 1 / 1 | 6.5 / 8.0 | >= 4 |
| A2 single-tile regions: Forest (%) | 86.4 / 75.5 | 91.4 / 76.5 | 20 / 11 | <= 20 |
| A1 median region size: Hills | 1 / 1 | 1 / 1 | 5 / 8 | >= 4 |
| A2 single-tile regions: Hills (%) | 82.5 / 72.0 | 89.5 / 69.5 | 30 / 10 | <= 20 |
| A1 median region size: Mountains | 1 / 1 | 1 / 1 | 2.5 / 5.0 | >= 3 |
| A2 single-tile regions: Mountains (%) | 81.2 / 78.8 | 83.3 / 69.0 | 25 / 12 | <= 20 |
| A1 median region size: Tundra | 1 / 1 | 1 / 1 | 2 / 2 | >= 4 |
| A2 single-tile regions: Tundra (%) | 78.5 / 73.8 | 75 / 71 | 39.4 / 16.3 | <= 20 |
| A1 median region size: Arctic | 2 / 2 | 2 / 2 | 5 / 6 | >= 4 |
| A2 single-tile regions: Arctic (%) | 49.1 / 39.1 | 48.8 / 38.6 | 12.5 / 0.0 | <= 20 |
| A1 median region size: Swamp | 1 / 1 | 1 / 1 | 3 / 5 | >= 2 |
| A2 single-tile regions: Swamp (%) | 100 / 92 | 100 / 93 | 50 / 0 | <= 20 |
| A1 median region size: Jungle | 1 / 1 | 1 / 1 | 3 / 6 | >= 4 |
| A2 single-tile regions: Jungle (%) | 85.7 / 74.8 | 92.6 / 80.4 | 16.7 / 11.8 | <= 20 |
| A3 land tiles in regions of 1-2 tiles (%) | 52.4 / 43.9 | 42.4 / 37.7 | 5.6 / 3.5 | <= 10 |
| B1 arctic tiles more than 3 rows from a pole | 38 / 25 FAIL | 47 / 32 FAIL | 0 / 0 PASS | 0 |
| B2 desert or jungle beyond 60% latitude | 9 / 2 FAIL | 4 / 2 FAIL | 0 / 0 PASS | 0 |
| B3 forbidden neighbours (jungle by desert/arctic, desert by arctic/swamp) | 14 / 12 FAIL | 9 / 7 FAIL | 0 / 0 PASS | 0 |
| B4 latitude ordering arctic > tundra > forest > grass/plains > desert/jungle (steps out of order) | 2 / 2 FAIL | 2 / 2 FAIL | 0 / 0 PASS | 0 |
| C1 mountain tiles in ranges of 3 or more (%) | 39.2 / 52.7 | 42.9 / 57.7 | 88.2 / 99.2 | >= 70 |
| C2 mountain ranges with hills beside them (%) | 43.5 / 67.3 | 22.2 / 66.7 | 50 / 78 | >= 60 |
| D1 river systems reaching the sea or a lake (%) | 100 / 100 | 85.7 / 100.0 | 100 / 100 | 100 |
| D2 river sources beside hills or mountains (%) | 44.4 / 79.2 | 44.4 / 56.3 | 66.7 / 77.5 | >= 50 |
| D3 junctions joining more than three channels | 1 / 0 | 1 / 0 | 1 / 0 | 0 |
| D4 river tiles within 3 rows of a pole | 0 / 0 | 0 / 0 | 0 / 0 | 0 |

## Extreme-settings worlds: E. balance, smoothed against classic

Terrain shares and the average food, shields and trade per tile are measured over each world's non-arctic land, rivers apart (they never change, E3); the arctic difference is on its own line.

| Check | worst / median | Target |
|---|---|---|
| E1 land tiles changed (count) | 0.0 / 0.0 | 0 |
| E2 coastline tiles changed (count) | 0.0 / 0.0 | 0 |
| E3 river tiles changed (count) | 0.0 / 0.0 | 0 |
| E0 arctic tiles changed (count, report only) | 38.0 / 25.0 | report only |
| E4 largest change in a terrain share of non-arctic land (points) | 0.4 / 0.3 | <= 2 |
| E5 average food per non-arctic land tile changed (%) | 0.1 / 0.0 | <= 3 |
| E5 average shields per non-arctic land tile changed (%) | 0.6 / 0.3 | <= 3 |
| E5 average trade per non-arctic land tile changed (%) | 0.0 / 0.0 | <= 3 |
| E6 special resources changed (%) | 0.0 / 0.0 | <= 10 |
| E6 huts changed (%) | 0.0 / 0.0 | <= 10 |
| E7 continents changed (count) | 0.0 / 0.0 | 0 |

## Extreme-settings worlds: F. playability, smoothed against classic

Starts are the 7 best city sites at least 10 cells apart, scored by the food (x3), shields (x2) and trade of their 21-cell working area; a viable site scores 60 or more.

| Check | worst / median | Target |
|---|---|---|
| F1 starts below the classic median start (count) | 2.0 / 0.0 | <= 3 of 7 |
| F2 start spread best-worst, smoothed minus classic | 6.0 / 3.5 | <= 0 |
| F3 viable city sites changed (%) | 5.1 / 2.4 | <= 10 |

## G. Regression and determinism (all worlds)

| Check | Result | Target |
|---|---|---|
| G1 classic and youngest-age worlds differing from the baseline | 0 PASS | 0 |
| G2 worlds differing between two runs of the same seeds and settings | 0 PASS | 0 |
