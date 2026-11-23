# v6: screens beyond the map

v4 and v5 settled the map. This round designs the screens around it: the city screen, the science screens, the front
end, and the interface kit that every other dialog, report and menu will share. Each prompt lists what the game's code
actually draws on that screen, so a picked sheet can be built as drawn.

## Sheets

| Prompt | What it shows | Content taken from |
| --- | --- | --- |
| `prompt-city.txt` | City screen, ancient Babylon, size 8, INFO tab | `CityWorker.cs`: citizens and specialists, City Resources, Units Supported, the 21-square map, INFO / HAPPY / MAP / VIEW, improvements with MORE and WONDERS, Food Storage, CHANGE / BUY / AUTO, RENAME, EXIT |
| `prompt-city-busy.txt` | The same layout stressed: medieval London, size 16, civil disorder, HAPPY tab, waste and corruption, pollution, a full improvements list | same, plus the disorder and sell dialogs |
| `prompt-science.txt` | Choose research, science report, advance discovered (Greeks) | `GameEngine.cs` (the "Which research field…" prompt), `Overlay_14.cs` `ScienceReport` ("Researching X (n/m)" and the known list). The advances follow the game's prerequisites: Astronomy needs Mysticism and Mathematics, both known |
| `prompt-frontend.txt` | Title and main menu, difficulty, tribe and leader name, customize world | `StartGame.cs` and `StartGameMenu.cs`: the five menu lines, the five levels, LAND MASS / TEMPERATURE / CLIMATE / AGE |
| `prompt-kit.txt` | Every layer-B component on one sheet: frames, dialogs, menu bar and drop-down, seal buttons in three states, tabs, badges, progress bars, resource icons, a report ledger | the boxes, menus and reports used across the game |

`style.txt` is added to the end of every prompt. It holds the v5 rule that the ukiyo-e look is the technique, not the
subject, plus the interface colours from `themes/ukiyo-e/hd/ui/colors.txt`.

Typefaces are not part of the kit sheet. An image model invents its lettering, so candidate fonts are compared by
setting them in the game's real strings and widths instead.

## Running

Run from the repository root:

```sh
OPENAI_API_KEY=... sh docs/art-exploration/ukiyo-v6/run.sh            # all sheets, 3 takes each
OPENAI_API_KEY=... sh docs/art-exploration/ukiyo-v6/run.sh city kit   # some sheets
TAKES=1 OPENAI_API_KEY=... sh docs/art-exploration/ukiyo-v6/run.sh science
```

It calls `scripts/concept_v4.py` (gpt-image-2, 1536x1024, medium quality) once per take, with the references listed in
`run.sh`. It writes `SHEET-N.png` and a greyscale `SHEET-N-grey.jpg`, and skips takes that already exist.

`city-busy` uses the chosen city take as its layout reference, so it runs second: pick a city take, copy it to
`city-pick.jpg`, then run `run.sh city-busy`. All sheets at 3 takes each make 15 images.

## Reviewing

- Check each sheet against the "Content taken from" column: every element the code draws should appear. v4's city
  screen left out the units panel, specialists, pollution and the buttons.
- Use the greyscale copies to check that the panels, highlights and buttons still separate without colour.
- In the busy city, check that rows stay aligned and the screen still reads at a glance.
- Write the chosen take of each sheet, and what to change, into this README.
