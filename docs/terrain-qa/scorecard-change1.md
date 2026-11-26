# Terrain QA scorecard

18 fixture worlds (tools/isoproof/terrain-qa/fixtures.json), modes: classic, previous, smoothed, commit change 1.

Default worlds (pass/fail): classic 29 of 41 failed, previous 14 of 41 failed, smoothed 17 of 54 failed. Extreme-settings worlds (climate checks only): classic 4 of 4 failed, previous 0 of 4 failed, smoothed 0 of 4 failed; their other checks are report only.

## Default worlds (10): data checks

Each cell: worst case across the worlds / median, then PASS or FAIL on the worst case.

| Check | classic | previous | smoothed | Target |
|---|---|---|---|---|
| A1 median region size: Desert | 1 / 1 FAIL | 3 / 6 FAIL | 1 / 6 FAIL | >= 4 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Desert (%) | 85.7 / 64.6 FAIL | 40 / 0 FAIL | 60 / 0 FAIL | <= 20 |
| A1 median region size: Plains | 1 / 1 FAIL | 8 / 10 PASS | 5 / 7 PASS | >= 4 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Plains (%) | 68.4 / 56.8 FAIL | 16.7 / 0.0 PASS | 10.5 / 2.3 PASS | <= 20 |
| A1 median region size: Grassland | 1 / 1 FAIL | 6 / 10 PASS | 5 / 7 PASS | >= 4 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Grassland (%) | 69.1 / 66.1 FAIL | 20 / 6 PASS | 13.8 / 4.8 PASS | <= 20 |
| A1 median region size: Forest | 1 / 1 FAIL | 6 / 8 PASS | 4.5 / 6.0 PASS | >= 4 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Forest (%) | 80.7 / 74.1 FAIL | 37.5 / 8.7 FAIL | 27.8 / 0.0 FAIL | <= 20 |
| A1 median region size: Hills | 1 / 1 FAIL | 5 / 7 PASS | 2 / 6 FAIL | >= 4 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Hills (%) | 81.4 / 75.2 FAIL | 23.1 / 11.7 FAIL | 50 / 20 FAIL | <= 20 |
| A1 median region size: Mountains | 1 / 1 FAIL | 2 / 4 FAIL | 1 / 4 FAIL | >= 3 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Mountains (%) | 89.7 / 81.0 FAIL | 33.3 / 15.5 FAIL | 55.6 / 33.3 FAIL | <= 20 |
| A1 median region size: Tundra | 1 / 1 FAIL | 3 / 3 FAIL | 2 / 4 FAIL | >= 4 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Tundra (%) | 82.8 / 75.6 FAIL | 19.0 / 7.0 PASS | 27.3 / 7.0 FAIL | <= 20 |
| A1 median region size: Arctic | 2 / 2 FAIL | 4.5 / 6.0 PASS | 4.5 / 6.0 PASS | >= 4 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Arctic (%) | 45.9 / 38.2 FAIL | 6.7 / 0.0 PASS | 6.7 / 0.0 PASS | <= 20 |
| A1 median region size: Swamp | 1 / 1 FAIL | 2 / 6 PASS | 2 / 4 PASS | >= 2 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Swamp (%) | 96.7 / 90.8 FAIL | 33.3 / 7.1 FAIL | 50 / 19 FAIL | <= 20 |
| A1 median region size: Jungle | 1 / 1 FAIL | 3 / 6 FAIL | 3.5 / 6.0 FAIL | >= 4 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Jungle (%) | 88.6 / 82.7 FAIL | 44.4 / 17.4 FAIL | 25 / 17 FAIL | <= 20 |
| A4 regions over 30 tiles or 40% of their landmass (count) | 1 / 0 FAIL | 10 / 9 FAIL | 7 / 2 FAIL | 0 |
| A4b largest region (tiles) | 30 / 19 PASS | 82 / 60 PASS | 42 / 35 PASS | report |
| A5 landmasses of 8+ tiles with one terrain, or 20+ with fewer than three (count) | 0 / 0 PASS | 3 / 2 FAIL | 0 / 0 PASS | 0 |
| A6 largest share of one terrain on a 20+ landmass (%) | 60.9 / 52.8 PASS | 100 / 97 FAIL | 64.7 / 47.8 PASS | <= 65 |
| H1 cold land (>55 deg) that is forest, tundra, hills or mountains (%) | 53.7 / 59.9 PASS | 62.0 / 77.5 PASS | 54.5 / 72.7 PASS | >= 40 worst, >= 55 median |
| H2 cold land (>55 deg) that is plains (%) | 11.4 / 4.8 PASS | 27.3 / 17.9 PASS | 31.4 / 18.3 FAIL | <= 30 |
| H3 single-terrain plains landmasses of 8+ tiles beyond 55 deg (count) | 0 / 0 PASS | 0 / 0 PASS | 0 / 0 PASS | 0 |
| I forest tiles at 0-25 deg (count, report only) | 35 / 26 PASS | 5 / 0 PASS | 13 / 8 PASS | report |
| I forest tiles at 25-55 deg (count, report only) | 82 / 56 PASS | 70 / 46 PASS | 80 / 50 PASS | report |
| I forest tiles at 55-90 deg (count, report only) | 26 / 18 PASS | 80 / 54 PASS | 59 / 52 PASS | report |
| A3 land tiles in regions of 1-2 tiles (%) | 52.0 / 47.0 FAIL | 5.0 / 3.3 PASS | 3.9 / 3.0 PASS | <= 10 |
| B1 arctic tiles more than 3 rows from a pole | 43 / 28 FAIL | 0 / 0 PASS | 0 / 0 PASS | 0 |
| B2 desert or jungle beyond 60% latitude | 7 / 6 FAIL | 0 / 0 PASS | 0 / 0 PASS | 0 |
| B3 forbidden neighbours (jungle by desert/arctic, desert by arctic/swamp) | 14 / 7 FAIL | 0 / 0 PASS | 0 / 0 PASS | 0 |
| B4 latitude ordering arctic > tundra > forest > grass/plains > desert/jungle (steps out of order) | 3 / 2 FAIL | 0 / 0 PASS | 1 / 0 FAIL | 0 |
| C1 mountain tiles in ranges of 3 or more (%) | 12.8 / 41.9 FAIL | 87.5 / 92.4 PASS | 87.5 / 92.6 PASS | >= 70 |
| C2 mountain ranges with hills beside them (%) | 54.3 / 69.2 FAIL | 62.5 / 71.4 PASS | 62.5 / 75.0 PASS | >= 60 |
| D1 river systems reaching the sea or a lake (%) | 100 / 100 PASS | 100 / 100 PASS | 100 / 100 PASS | 100 |
| D2 river sources beside hills or mountains (%) | 55.6 / 81.8 PASS | 66.7 / 78.9 PASS | 55.6 / 80.9 PASS | >= 50 |
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
| F1 starts below the classic median start (count) | 3.0 / 0.0 PASS | <= 3 of 7 |
| F2 start spread best-worst, smoothed minus classic | 16.0 / 4.0 FAIL | <= 0 |
| F3 viable city sites changed (%) | 5.3 / 3.1 PASS | <= 10 |

## Extreme-settings worlds (8): data checks

Each cell: worst case across the worlds / median; climate checks (B) are PASS/FAIL, the rest report only.

| Check | classic | previous | smoothed | Target |
|---|---|---|---|---|
| A1 median region size: Desert | 1 / 1 | 2 / 17 | 4 / 9 | >= 4 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Desert (%) | 77.8 / 67.6 | 37.5 / 0.0 | 12.5 / 0.0 | <= 20 |
| A1 median region size: Plains | 1 / 1 | 5 / 12 | 5 / 7 | >= 4 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Plains (%) | 58.2 / 56.0 | 25 / 4 | 12 / 5 | <= 20 |
| A1 median region size: Grassland | 1 / 1 | 5.5 / 12.2 | 5 / 7 | >= 4 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Grassland (%) | 71.3 / 66.1 | 31.2 / 2.9 | 14.3 / 0.0 | <= 20 |
| A1 median region size: Forest | 1 / 1 | 6.5 / 8.0 | 4 / 5 | >= 4 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Forest (%) | 86.4 / 75.5 | 20 / 11 | 23.1 / 9.7 | <= 20 |
| A1 median region size: Hills | 1 / 1 | 5 / 8 | 4.5 / 8.2 | >= 4 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Hills (%) | 82.5 / 72.0 | 30 / 10 | 30 / 15 | <= 20 |
| A1 median region size: Mountains | 1 / 1 | 2.5 / 5.0 | 1.5 / 4.5 | >= 3 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Mountains (%) | 81.2 / 78.8 | 25 / 12 | 50 / 20 | <= 20 |
| A1 median region size: Tundra | 1 / 1 | 2 / 2 | 2 / 2 | >= 4 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Tundra (%) | 78.5 / 73.8 | 39.4 / 16.3 | 39.4 / 15.9 | <= 20 |
| A1 median region size: Arctic | 2 / 2 | 5 / 6 | 5 / 6 | >= 4 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Arctic (%) | 49.1 / 39.1 | 12.5 / 0.0 | 12.5 / 0.0 | <= 20 |
| A1 median region size: Swamp | 1 / 1 | 3 / 5 | 3.5 / 5.0 | >= 2 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Swamp (%) | 100 / 92 | 50 / 0 | 28.6 / 6.2 | <= 20 |
| A1 median region size: Jungle | 1 / 1 | 3 / 6 | 3 / 4 | >= 4 (Simon round 3: 5-14 median) |
| A2 single-tile regions: Jungle (%) | 85.7 / 74.8 | 16.7 / 11.8 | 27.3 / 4.5 | <= 20 |
| A4 regions over 30 tiles or 40% of their landmass (count) | 2 / 0 | 14 / 9 | 8 / 2 | 0 |
| A4b largest region (tiles) | 32 / 20 | 163 / 75 | 54 / 40 | report |
| A5 landmasses of 8+ tiles with one terrain, or 20+ with fewer than three (count) | 0 / 0 | 3 / 2 | 0 / 0 | 0 |
| A6 largest share of one terrain on a 20+ landmass (%) | 70.8 / 42.9 | 100 / 79 | 57.1 / 52.8 | <= 65 |
| H1 cold land (>55 deg) that is forest, tundra, hills or mountains (%) | 59.8 / 72.5 | 58.4 / 78.0 | 57.6 / 70.9 | >= 40 worst, >= 55 median |
| H2 cold land (>55 deg) that is plains (%) | 6.2 / 3.4 | 41.6 / 20.9 | 27.0 / 19.4 | <= 30 |
| H3 single-terrain plains landmasses of 8+ tiles beyond 55 deg (count) | 0 / 0 | 0 / 0 | 0 / 0 | 0 |
| I forest tiles at 0-25 deg (count, report only) | 38 / 29 | 3 / 0 | 14 / 6 | report |
| I forest tiles at 25-55 deg (count, report only) | 60 / 42 | 53 / 46 | 62 / 46 | report |
| I forest tiles at 55-90 deg (count, report only) | 15 / 7 | 71 / 38 | 52 / 28 | report |
| A3 land tiles in regions of 1-2 tiles (%) | 52.4 / 43.9 | 5.6 / 3.5 | 4.7 / 2.9 | <= 10 |
| B1 arctic tiles more than 3 rows from a pole | 38 / 25 FAIL | 0 / 0 PASS | 0 / 0 PASS | 0 |
| B2 desert or jungle beyond 60% latitude | 9 / 2 FAIL | 0 / 0 PASS | 0 / 0 PASS | 0 |
| B3 forbidden neighbours (jungle by desert/arctic, desert by arctic/swamp) | 14 / 12 FAIL | 0 / 0 PASS | 0 / 0 PASS | 0 |
| B4 latitude ordering arctic > tundra > forest > grass/plains > desert/jungle (steps out of order) | 2 / 2 FAIL | 0 / 0 PASS | 0 / 0 PASS | 0 |
| C1 mountain tiles in ranges of 3 or more (%) | 39.2 / 52.7 | 88.2 / 99.2 | 88.2 / 99.1 | >= 70 |
| C2 mountain ranges with hills beside them (%) | 43.5 / 67.3 | 50 / 78 | 50 / 80 | >= 60 |
| D1 river systems reaching the sea or a lake (%) | 100 / 100 | 100 / 100 | 100 / 100 | 100 |
| D2 river sources beside hills or mountains (%) | 44.4 / 79.2 | 66.7 / 77.5 | 66.7 / 77.5 | >= 50 |
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
| F1 starts below the classic median start (count) | 3.0 / 0.5 | <= 3 of 7 |
| F2 start spread best-worst, smoothed minus classic | 12.0 / 4.5 | <= 0 |
| F3 viable city sites changed (%) | 5.1 / 2.7 | <= 10 |

## G. Regression and determinism (all worlds)

| Check | Result | Target |
|---|---|---|
| G1 classic and youngest-age worlds differing from the baseline | 0 PASS | 0 |
