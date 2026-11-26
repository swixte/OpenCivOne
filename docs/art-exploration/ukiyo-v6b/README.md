# v6b: fixes to the v6 picks

v6b builds on the v6 picks (city 2, science 1, front end 2, kit 1). It fixes what the v6 review found and runs the
busy-city stress test. `run.sh` makes 2 takes per sheet, using the v6 pick as the first reference image, and appends
`style.txt` to each prompt.

## What changed from v6

| Sheet | Change |
| --- | --- |
| all (`style.txt`) | Asks for a bold, even key line and flat colour, not a storybook look; neutral ornaments that are the same for every civilization; invented emblems, no real flags or inscriptions; seals with a geometric mark instead of writing |
| `city` | Redraws v6 take 2 (`city-pick.jpg`) without the Babylonian brick band |
| `city-busy` | First run: London, size 16, civil disorder, HAPPY tab, waste and corruption, pollution, full building list with SELL |
| `science-*` | The v6 sheet split into three landscape full screens: `choose`, `report`, `discover` |
| `frontend` | Named invented emblems for the seven tribes; a square-spiral seal for the menu highlight |
| `kit` | Woodblock citizen heads instead of emoji faces; the v6 kit-1 button order |

## Picks

| Sheet | Pick | Notes |
| --- | --- | --- |
| City | 2 | Brick band gone, layout kept. The linework barely changed: the v6 reference outweighs the style text. |
| Busy city | 1 | All 16 citizens with labelled specialists; waste and corruption crossed out; pollution squares; 10 buildings with SELL. The layout holds at this load. Fix: the St George's cross shields, and a ziggurat used as London's Temple. |
| Choose research | 1 | The dialog sits over a real-looking main screen; the seal is a square spiral. |
| Science report | 1 | Uses the full width. In take 2 the portrait overlaps the lamp bar. |
| Advance discovered | 2 | Stronger linework; picture and text split about 3:2. |
| Front end | 1 | Invented emblems on every banner; geometric seal. |
| Kit | 2 | Citizen heads replace the emoji. Fix: the "no writing on seals" rule also blanked the OK button, and in take 1 the EXIT button too. Button labels need an exception. |

## For the next round

- **Linework.** A v6 take as the first reference pins the soft rendering. Try Hokusai and Hiroshige prints as the only
  style references, with the v6 pick described in words, or post-process to a key-line pass.
- **Seals.** Seal marks carry no writing, but seal-shaped buttons keep their labels (OK, EXIT, BUY).
- **Emblems in scenes.** The no-real-emblem rule has to reach figures and shields inside scenes too, not only banners.
- **Buildings.** Building icons follow the city's civilization, not the first reference.
