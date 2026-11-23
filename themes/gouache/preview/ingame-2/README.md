# Gouache theme in play: check of commit e7b8b38

The real game ran in the cloud under Xvfb (1600x1000, window 1282x802, map at 2x), driven with xdotool and captured
with ImageMagick. It used the stand-in data set (`tools/standin`), the `themes/gouache/hd` files copied into
`~/Dos/Civ1/themes/gouache/`, and `Preferences.json` set to `{"Theme": "gouache", "AnimateWater": true,
"Debug": {"DebugMode": true}}`. The game started from Start a New Game with the defaults (Chieftain, Romans).
Files ending in `-3x` are crops enlarged 3x with nearest-neighbour, so each game pixel is a 3x3 block.

## How it was run, and what was changed to get there

- **Toolchain.** The Ubuntu `dotnet-sdk-8.0` (8.0.1xx) and the MS 8.0.4xx SDK both fail with 111 errors, because
  Avalonia 12's XAML generator needs Roslyn 4.14 (warning CS9057), so no `InitializeComponent` or named controls
  get generated. The build only works with the **.NET 10 SDK** (10.0.404, still targeting net8.0). README or
  `global.json` should say this.
- **Two crashes in `GameEngine.F0_1ade_0421_SelectCityProduction` stopped every game at the first "What shall we
  build" prompt.** Both came in with a3008f3 ("Refine SelectCityProduction code"). They are fixed in the first
  commit on this branch. That commit is separate, so you can cherry-pick it or drop it.
  1. The unit filter was inverted: it listed only sea units, and only in coastal cities. An inland city got an
     empty list, and a later lookup went out of range. The original condition is restored: list a unit when the
     city has water access, or when the unit is not a sea unit.
  2. `buildIDList[ShowMenuBox(...) - 1]`: `ShowMenuBox` returns a 0-based option index, and `buildIDList[0]` holds
     the current production, so choosing the first item read index -1. It is now `+ 1`. I checked it in play:
     choosing Settlers builds Settlers.
- **Town sizes were set in the save file, not grown in play.** The game ran 60+ turns (Rome grew to size 3 and
  built a militia and settlers), but early AI cities stay at size 1–2, so the town ladder would never have been
  reached in a sensible time. I saved the game, then edited each city's size and its palace and walls bits in
  `CIVIL*.SVE` (city table at byte offset 5384, 28 bytes per city; walls are bit 7, `0x80`), and loaded it again.
  Everything you see is still drawn by the game's own draw path.

## The shots

| File | What it shows |
|---|---|
| `01-rome-founded.png`, `01-rome-founded-3x.png` | The turn Rome is founded with **B**: a size-1 hamlet and its grey plaque "1". |
| `01b-rome-size1-with-settler.png` | Rome a few turns later, with its black city square, the irrigated tile and a Roman settler beside it. |
| `02-town-ladder-3x.png` | The ladder in play: size 2 hamlet (Tenochtitlan), 3 (Rome), 6 (Athens), 10 (Zimbabwe), 14 (Paris), and 9 with palace = capital (Samarkand). Each plaque is in its owner's colour. |
| `02b-walled-towns-3x.png` | The same cities with City Walls: sizes 2, 6, 10, 14, and capital plus walls. |
| `02c`–`02e` | Full frames: Zimbabwe (size 10), Samarkand (capital), Paris (size 14, walled). |
| `03-units-on-bases-3x.png` | Roman settlers (white base, two states), a Greek unit (pink base), and a unit of a blue civ on a hill. |
| `03b-greek-and-blue-units-3x.png`, `03c-athens-two-civs.png` | Two civs' units next to Athens, and fortified units inside the fortress outline. |
| `04-irrigation.png`, `04-irrigation-3x.png` | Plains next to Rome irrigated by settlers (**I**), done in play. |
| `05-bonus-shields-fortified-units-3x.png`, `05b-peking-grassland.png` | Grassland bonus shields around Peking, two fortified units, wheat and a horses special. |

## Verdict (judged harshly)

**What works.** The sprite path works end to end in the real game. The theme loads, the hamlet → town → city →
capital art is chosen by size, the walled variants are swapped in for the larger steps, units stand on bases in
their owner's colour, painted irrigation replaces the old dots, and the grassland shield is painted. The terrain
blends are soft, and no tile seams were visible in any frame. Nothing crashed in the draw code.

**Town ladder: reads, but the bottom of the ladder is weak.**
- Steps 2 → 3 → 4 → capital read clearly: the towns get denser, then the dome appears, then the white palace.
- The size 1–2 hamlet is too small and too pale. It is a beige blob about a third of the cell wide, and it looks
  smaller than the goody-hut village one tile away (see `01-rome-founded-3x`). A player's new capital should never
  read as less important than a hut.
- Steps 0 and 1 are too alike at game size. They differ mostly in roof colour.
- **The walled art for steps 0–2 shows no walls in play** (`02b`: the size 2 and size 6 cities look exactly like the
  unwalled ones). Either those three walled slots (SP257 x=225, 241, 257) are missing their wall ring, or the ring is
  too faint to see. Steps 3, 4 and capital show a clear wall square.

**Size plaque: legible and small, mostly.**
- It is about 10 game px high and sits in the cell's top-left corner, with a 1 px dark rim. Digits are readable at
  2x for every colour tried: grey, green, red, blue, cyan. Two-digit sizes (10, 14) fit.
- The pale-green plaque (Aztecs) and the grey plaque (Romans) have the weakest contrast against grassland and plains.
- In the founding frame (`01-rome-founded-3x`) the plaque floated about half a cell above and left of the hamlet,
  with no black square. Later frames of the same city are correct, so this may be a one-frame redraw problem.
  Worth checking once.

**The black city square is the worst thing on the map.** It is a hard 2 px pure-black frame drawn round every
city cell, and in DebugMode round AI cities too. It cuts straight through the gouache and boxes in the town art,
and it is the most contrasty thing on screen. It reads as a selection cursor, not as "this city has a defender".

**Units on bases.**
- The bases are centred in the cell, and the figures sit centred on them (Greek, blue civ, active Roman settler).
- The bases are only about 55–60 % of the cell wide. They read like tokens, which is right for "board game", but
  infantry figures are tiny at 1x.
- **A sentried or inactive Roman settler is drawn fully grey-white** (`03`, first panel): the wagon, the ox and the
  figure all turn the base's white. The active settler next to it keeps its browns. Either the inactive tint is far
  too strong, or the player-colour key range is catching the sprite's own greys and creams. The Romans are white,
  so this is the worst case for key-colour leakage.
- The Greek unit's spear or pole runs past the right edge of its base (`03`, third panel). It does not clip at the
  cell edge, but it breaks the "everything sits on its disc" rule.

**Irrigation.** The painted field patches with blue cross-channels read clearly as farmland. But:
- The patch is softer and blurrier than the surrounding terrain, as if upscaled. Its square, faded edge looks like
  a decal stamped onto the tile, not painted into it.
- The channels do not connect to the river one tile away.

**Grassland shield.** The painted red heraldic shield reads as "bonus" instantly. But it is the most saturated
red on the map, sits on every second grassland tile, and competes with the city plaques and the red player colour.
On a big grassland area it looks like a scatter of UI markers, not terrain.

**Not part of this commit, but it hurts the shots.** The cyan pixel-font city names are huge next to the painted
art and get cut off at the screen edge ("Zimbabw", "Samark."). The original-style side panel and minimap are
still the DOS look.

## Concrete fixes, in priority order

1. **Black city square:** replace it with a 1 px soft frame in the owner's colour, or a drop shadow under the
   town, or drop it on the painted map and show "has defender" with a small mark on the plaque.
2. **Walled steps 0–2:** paint a visible palisade or low wall ring (a dark outline at least 1 game px) into SP257
   x=225/241/257, so walls read at every size.
3. **Hamlet:** scale it up to about 60 % of the cell, add darker roofs and a cast shadow, and make step 1 clearly
   bigger than step 0 (more buildings, not only different colours). Then step 0 always outweighs a goody hut.
4. **Grey/white key colours:** restrict the key-colour mask to the base and pennant pixels only, and check every
   Roman (white) sprite. Find out why the inactive settler turns fully grey.
5. **Bonus shield:** desaturate it by about 30 % and shrink it to about 70 %, or let it take the terrain's tone,
   so it stays a terrain cue.
6. **Irrigation:** paint the patch at the native HD scale (it currently looks upscaled), break up the square edge
   to match the groundblend, and join the channels to an adjacent river or coast where there is one.
7. **Plaque:** give the pale-green and grey player colours a darker rim or fill, and check the plaque position on
   the frame a city is founded.
8. **Unit art:** keep weapons inside the base's circle (the Greek pole). Consider bases about 10 % larger so foot
   units stay readable at 1x.
9. **Build docs:** say that the .NET 10 SDK is needed to build. Keep the two `SelectCityProduction` fixes, or
   reproduce them, since without them no new game gets past the first build prompt.
