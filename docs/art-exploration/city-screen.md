# City screen and the screens beyond the map (v6–v13)

v2–v5 settled the map. These rounds look at everything else: the city screen first, then science, the front end and the
interface kit every dialog, report and menu would share. As with the rest of this folder, nothing here is wired into
the game.

Open these two pages in a browser:

- [`screen-plan/index.html`](screen-plan/index.html): the inventory of every non-map screen, read from the code, with
  the three-layer approach (HD type, a chrome kit, rebuilt anchor screens) and the v6, v6b, v8 and v9 rounds.
- [`city-screen-mockup/index.html`](city-screen-mockup/index.html): the city screen as a working page. Serve the folder
  (`python -m http.server --directory docs/art-exploration/city-screen-mockup`) so its images load.

## Where it ended

The city screen keeps **Civ1's layout**: every panel sits at its original 320×200 rectangle times four, read from
`CityWorker.cs`, in the ukiyo-e theme's re-mapping of the original colours, with the panels tiled in the theme's
painted slate (its version of the SP299 city-screen pattern). Icons and citizens are the theme's own painted SP257
pieces, so they match the map and the rest of the game.

- **Only two things are art**, each in a thin gold frame: the city map and the city view. Everything else is flat UI
  drawn in code, so the paintings are the only soft things on the screen.
- **The city map** is the 21 squares in the game's diamond layout, composed from the theme's isometric tiles at the
  game's 80×40 tile size (`ukiyo-v10/citymap_iso.py`).
- **The city view** is a town screen seen from the front at eye level, built like a stage set: one painted backdrop,
  each improvement a front-facing cut-out at a fixed place and depth, drawn back to front so nearer buildings overlap
  farther ones (`ukiyo-v13/compose.py`). As the city grows, houses pack the walls, then spread onto the plain, with
  more people, carts, stalls and boats. VIEW can be shown full screen, as Civ1's was.
- **Behaviour follows the code**: unit support (free up to the city's size, then a shield each; settlers eat food;
  diplomats free), corruption, trade routes (`{Ur:+1}`), the HAPPY rows in Civ1's order (base, luxuries, buildings,
  martial law, wonders), selling once a turn for the full cost, Civ1's cost to complete, AUTO, the size-5 rule for
  taxmen and scientists, the keys, and the read-only diplomat's report. Units are disbanded only when the city cannot
  pay for them; the original has no click to disband.

Not covered yet: 7 of Civ1's improvements and none of the wonders have cut-outs; the `M` key's purpose is unclear from
the code; the MAP tab's world is made up.

## Rounds

| Folder | What it tried | Outcome |
| --- | --- | --- |
| `ukiyo-v6/` | Concept sheets for the city screen, science, front end and an interface kit, each prompt listing what the code draws (`run.sh`, `README.md`) | Layouts worked; the look was a soft storybook illustration, with real flags, fake kanji and emoji faces |
| `ukiyo-v6b/` | Fixes to the v6 picks, the size-16 "busy city" stress test, science as three landscape screens | The busy city held up; linework stayed soft because the v6 takes were the references |
| `ukiyo-v7/` | A Heroes-style city screen: the whole city always in view, panels around it | Two takes; set aside for a different look |
| `ukiyo-v8/` | One Babylon, one isometric layout sketch as the only reference, four print traditions: byōbu, Hiroshige, aizuri-e, shin-hanga | The byōbu folding screen was chosen |
| `ukiyo-v9/` | The byōbu city screen as a painted whole, and a 1280×800 blueprint wireframe (`wireframe.py`) | One painting for everything blurred UI and art; the next rounds keep the art to two panels |
| `ukiyo-v10/` | The city map: a painted top-down map, then the map composed from the game's own isometric tiles | The tile version is used |
| `ukiyo-v11/` | The city view assembled from parts on the city's real land (sprites in the map tiles' style) | Worked, but the style drifted from the byōbu painting |
| `ukiyo-v12/` | The same in the painting's own style and proportions: a low wall ring, many courtyard houses, landmarks | Matched the painting; still a view from above |
| `ukiyo-v13/` | The town screen seen from the front, with town life and growth by size | Used in the mock-up |

## Making the pictures

Prompts sit beside the pictures. Generated takes are kept as JPG; the magenta sprite sheets stay PNG because the cut-outs
are keyed from them.

- `scripts/concept_v4.py` makes one image. `QUALITY` (`low`, `medium`, `high`) and `SIZE` (default `1536x1024`) change
  the request; v8 onward used `QUALITY=high`, the city map `SIZE=1024x1024`. It needs `OPENAI_API_KEY`.
- `ukiyo-v6/run.sh` and `ukiyo-v6b/run.sh` run a whole round of sheets.
- `ukiyo-v11/cut.py` and `ukiyo-v12/cut.py` cut a round's sheets into `sprites/` (not committed); then `compose.py OUT`
  builds the city views. `ukiyo-v13/compose.py` cuts its sheets itself. All of them use
  `tools/hdart/painted_assets.py` for the magenta key and run from their own folder.

## Lessons

- **A generated screen paints its chrome too.** Painting the whole screen as one picture makes UI and art the same
  texture. Keep the art to the panels that are pictures, and draw the rest.
- **The first reference image wins.** Using an earlier take as a reference copies its rendering more than any style
  words change it (v6b). For a new look, give only real prints, or a plain layout sketch for composition (v8).
- **Name a real tradition.** "Ukiyo-e" alone gave a generic look; naming byōbu, Hiroshige, aizuri-e or shin-hanga, with
  limited inks and empty paper, read as hand-made.
- **Keep the game's iconography.** Civ1's own symbols and citizens, and the map's tile art, make the screen read as the
  same game.
- **A view per city is assembled, not painted.** Fixed places for improvements, houses that fill by size, and the
  city's real land are what Civ1's CITYPIX did and what a town screen needs.
- **Rules stay culture-neutral.** No real flags or national emblems; seals carry geometric marks, but buttons keep their
  labels.
