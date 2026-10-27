# v5: map readability

The ukiyo-e look is the drawing technique, not the subject: people, buildings and ships belong to each civilization
(Greek, Egyptian, English, Mongol here). This round fixes how the map reads. Every image has a greyscale copy
(`*-grey.jpg`) to check contrast without colour.

| File | What it shows |
| --- | --- |
| `palette.jpg` | Every terrain in one map, with the colour swatches |
| `screen.jpg` | Ancient era, Greeks against Egyptians |
| `readability.jpg` | The same city, unit and improvement on each of 12 terrains |
| `screen2.jpg` | Medieval era, English against Mongols, with the corrected swamp and tundra and stronger cities |

## Rules

**Terrain.** Each terrain has its own colour and brightness, at medium saturation, plus its own pattern:

| Terrain | Colour | L* | Pattern |
| --- | --- | --- | --- |
| Deep sea | `#1F3A5F` | 24 | Hokusai waves |
| Forest | `#2E5A43` | 35 | dense canopy |
| Swamp | `#6B6B45` | 44 | reeds, dark pools |
| Jungle | `#2F8A4E` | 51 | palms |
| Mountains | `#6E7C8C` | 51 | peaks, snow caps |
| River | `#3F7FBF` | 52 | light edge |
| Hills | `#A08A4E` | 58 | contour lines |
| Shallow coast | `#4F9BA8` | 60 | ripples |
| Grassland | `#8DB255` | 68 | scattered trees |
| Tundra | `#B4A3B8` | 69 | lichen, snow patches |
| Plains | `#D9B45A` | 75 | tufts |
| Desert | `#EAD9A6` | 87 | dunes |
| Arctic | `#F2F2EE` | 95 | ice floes |

The closest two land colours are at least 21 apart (CIE76); in v5's first try forest and swamp were only 10 apart,
and swamp changed from `#4F6B55` to `#6B6B45`, tundra from `#A9A39A` to `#B4A3B8`.

**Cities blend in and pop.** A city stands on a ring of very light cleared earth (`#F0E6C8`) that fades into the land
with lanes and outlying houses, so it grows out of the ground. It pops by value as well as colour: the lightest ring,
a dark key line around its walls, and a soft shadow lower right. Vermilion roofs are kept for cities only, so they are
the brightest warm spot on the map. The size badge takes the player colour.

**Land improvements** are painted in a tone of the ground under them: irrigated fields a slightly brighter green
patchwork with thin bright-blue channels that follow the land; mines a dark spoil terrace with a timber headframe;
roads thin pale cream lines; railroads thin dark lines with sleepers.

**Units** are small tokens, about half a map square, with a thin cream outline and a strong player colour. Terrain
never uses strong player colours, so units stand out everywhere. Open issue: a yellow player on plains and desert is
weak (`screen2.jpg`, the Mongols); yellow needs a darker key line and outline, or a different player colour.
