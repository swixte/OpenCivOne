# Terrain QA

Simon's framework for judging terrain generation by the same worlds, measurements and pictures every round.

- `fixtures.json`: the fixed worlds. There are 10 seeds at default settings and 8 at the extremes of land mass, temperature, climate and age. Seeds are added, never replaced.
- Generate the worlds with the game's own generator, without a screen:
  `ISO_GENWORLDS=OUT dotnet run` in `tests/HDLayerTests`. It writes `NAME-classic.txt` (the fixture's settings) and `NAME-young.txt` (youngest age).
- `scorecard.py OUT scorecard.md COMMIT`: runs the data checks (sections A to D) and writes the scorecard.
- `baseline/`: the first run of the classic generator, with its dumps and scorecard. It is never overwritten.

Determinism: generating all fixtures twice gives byte-identical dumps.
