# Terrain QA scorecard

18 fixture worlds (tools/isoproof/terrain-qa/fixtures.json), modes: classic, young, smoothed, commit 93a3957 + smoothing.

Each cell: worst case across the worlds / median, then PASS or FAIL on the worst case.

| Check | classic | young | smoothed | Target |
|---|---|---|---|---|
| A1 median region size: Desert | 1 / 1 FAIL | 1 / 1 FAIL | 1 / 6 FAIL | >= 4 |
| A2 single-tile regions: Desert (%) | 100 / 67 FAIL | 85 / 68 FAIL | 100 / 16 FAIL | <= 20 |
| A1 median region size: Plains | 1 / 1 FAIL | 1 / 1 FAIL | 2 / 5 FAIL | >= 4 |
| A2 single-tile regions: Plains (%) | 68 / 57 FAIL | 60 / 52 FAIL | 39 / 30 FAIL | <= 20 |
| A1 median region size: Grassland | 1 / 1 FAIL | 1 / 1 FAIL | 2 / 5 FAIL | >= 4 |
| A2 single-tile regions: Grassland (%) | 71 / 66 FAIL | 70 / 60 FAIL | 39 / 26 FAIL | <= 20 |
| A1 median region size: Forest | 1 / 1 FAIL | 1 / 1 FAIL | 5 / 9 PASS | >= 4 |
| A2 single-tile regions: Forest (%) | 86 / 74 FAIL | 91 / 73 FAIL | 25 / 0 FAIL | <= 20 |
| A1 median region size: Hills | 1 / 1 FAIL | 1 / 1 FAIL | 1 / 3 FAIL | >= 4 |
| A2 single-tile regions: Hills (%) | 82 / 74 FAIL | 89 / 71 FAIL | 79 / 42 FAIL | <= 20 |
| A1 median region size: Mountains | 1 / 1 FAIL | 1 / 1 FAIL | 4 / 12 PASS | >= 3 |
| A2 single-tile regions: Mountains (%) | 90 / 79 FAIL | 90 / 73 FAIL | 33 / 0 FAIL | <= 20 |
| A1 median region size: Tundra | 1 / 1 FAIL | 1 / 1 FAIL | 1 / 1 FAIL | >= 4 |
| A2 single-tile regions: Tundra (%) | 83 / 75 FAIL | 77 / 72 FAIL | 81 / 68 FAIL | <= 20 |
| A1 median region size: Arctic | 2 / 2 FAIL | 2 / 2 FAIL | 2 / 3 FAIL | >= 4 |
| A2 single-tile regions: Arctic (%) | 49 / 38 FAIL | 49 / 36 FAIL | 32 / 21 FAIL | <= 20 |
| A1 median region size: Swamp | 1 / 1 FAIL | 1 / 1 FAIL | 1 / 4 FAIL | >= 2 |
| A2 single-tile regions: Swamp (%) | 100 / 92 FAIL | 100 / 91 FAIL | 60 / 12 FAIL | <= 20 |
| A1 median region size: Jungle | 1 / 1 FAIL | 1 / 1 FAIL | 4 / 14 PASS | >= 4 |
| A2 single-tile regions: Jungle (%) | 89 / 81 FAIL | 93 / 81 FAIL | 43 / 0 FAIL | <= 20 |
| A3 land tiles in regions of 1-2 tiles (%) | 52 / 46 FAIL | 43 / 38 FAIL | 14 / 10 FAIL | <= 10 |
| B1 arctic tiles more than 3 rows from a pole | 43 / 26 FAIL | 48 / 31 FAIL | 0 / 0 PASS | 0 |
| B2 desert or jungle beyond 60% latitude | 9 / 5 FAIL | 15 / 2 FAIL | 0 / 0 PASS | 0 |
| B3 forbidden neighbours (jungle by desert/arctic, desert by arctic/swamp) | 14 / 7 FAIL | 13 / 7 FAIL | 0 / 0 PASS | 0 |
| B4 latitude ordering arctic > tundra > forest > grass/plains > desert/jungle (steps out of order) | 3 / 2 FAIL | 3 / 2 FAIL | 2 / 1 FAIL | 0 |
| C1 mountain tiles in ranges of 3 or more (%) | 13 / 48 FAIL | 0 / 54 FAIL | 96 / 100 PASS | >= 70 |
| C2 mountain ranges with hills beside them (%) | 43 / 68 FAIL | 22 / 67 FAIL | 80 / 100 PASS | >= 60 |
| D1 river systems reaching the sea or a lake (%) | 100 / 100 PASS | 83 / 100 FAIL | 100 / 100 PASS | 100 |
| D2 river sources beside hills or mountains (%) | 44 / 82 FAIL | 44 / 65 FAIL | 100 / 100 PASS | >= 50 |
| D3 junctions joining more than three channels | 2 / 0 FAIL | 2 / 0 FAIL | 2 / 0 FAIL | 0 |
| D4 river tiles within 3 rows of a pole | 0 / 0 PASS | 0 / 0 PASS | 0 / 0 PASS | 0 |

## E. Balance: smoothed against classic, same world

| Check | worst / median | Target |
|---|---|---|
| E1 land tiles changed (count) | 0.0 / 0.0 PASS | 0 |
| E2 coastline tiles changed (count) | 0.0 / 0.0 PASS | 0 |
| E3 river tiles changed (count) | 0.0 / 0.0 PASS | 0 |
| E4 largest change in a terrain share (points) | 4.5 / 2.8 FAIL | <= 2 |
| E5 total food changed (%) | 6.1 / 2.9 FAIL | <= 3 |
| E5 total shields changed (%) | 6.0 / 2.4 FAIL | <= 3 |
| E5 total trade changed (%) | 0.0 / 0.0 PASS | <= 3 |
| E6 special resources changed (%) | 0.0 / 0.0 PASS | <= 10 |
| E6 huts changed (%) | 0.0 / 0.0 PASS | <= 10 |
| E7 continents changed (count) | 0.0 / 0.0 PASS | 0 |

Failed checks: classic 29 of 31, young 30 of 31, smoothed 23 of 31

