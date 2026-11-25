---
name: aesthetic-check
description: Visual quality gate for any change that affects how the game or a theme looks (art, sprites, map drawing, effects, UI). Run it before saying a visual change is done, finished, fixed or ready, and before committing one. Compares the result with the theme's reference renders, element by element, at game size and enlarged, and records a verdict for each.
---

# Aesthetic check

A visual change is not done until it passes this check. "It is visible", "it builds" and "it matches the code's intent" are not passes. The question is: **would the art owner accept this next to the reference renders?**

Drawing things in code is fine. What matters is that whatever is drawn, painted or generated sits in the same visual language as the art around it.

## 1. Gather the evidence

For every element the change touches, and every element next to it on screen:

1. **Reference:** the theme's reference renders. For gouache: `themes/gouache/preview/muted-map.jpg`, `muted-zoom.jpg` and `muted-values.jpg` (the muted, atlas-like direction, rendered in the game), and `themes/gouache/concept.png`. The older saturated renders in that folder (`v*-map.jpg`, `random-*`, `topview/`) show the look the theme moved away from: use them for what elements exist, not for colour. If none show the element, use the nearest element the art does show (e.g. the large walled towns for small walled ones).
2. **In the real game:** a screenshot from the running game, not only the test harness. Get the state there with a save edit if play won't reach it quickly. Note the exceptions you hit, e.g. units added by editing the save stay invisible until they move.
3. **Crops:** each element at **1x** (what a player sees) and at **3x** (nearest or Lanczos), set **side by side with the reference** in one image, then look at that image.
4. **Every case:** sizes, states and colours, e.g. every town step walled and open, every player colour, sentry/fortified/active, land and sea, start, middle and end of an animation.

## 2. Value and saturation test (before judging against the reference)

"Fits the reference" is not enough on its own: a reference can be loud, and matching it then makes things worse. So
first, on screenshots of the map from the real game, before and after the change:

```
python3 tools/hdart/valuecheck.py OUT.jpg before.png after.png
```

- **In grey** (the sheet it writes): the map must read in bands of value. Sea, open land and forest are distinct bands
  of darkness; towns read by their dark masses; nothing is a white flare (snow peaks, surf, bases) unless it is meant
  to draw the eye. A change that flattens the bands, or brings back a flare, fails.
- **Saturation caps** (the script fails a shot over them): median 60, 95th percentile 110, 99th 140. Colour comes
  from the painting's own variation, not from boosting it. A change must not raise the shot's saturation over a cap.
- **One light**: everything lit from the top left, warm in the light and cool in the shade (the engine's `grade`).

For the same world before and after, set `OPENCIVONE_SEED` (any number) when running the game.

## 3. Judge each element against these questions

Write the answer for each; a single "no" fails the element.

- **Same medium?** Does it look painted (soft edges, varied colour, light and shade, texture) where the art is painted? Flat fills, uniform strokes, perfect geometry, pure white or pure black lines and vector-clean shapes next to gouache are a **fail**. A drawn ring, outline or icon that does not look painted fails even if it reads well.
- **Same palette and value?** Do its colours and darkness come from the art around it (sample them), not from picked colours? Is it in its terrain's value band (step 2)? Nothing should be the brightest or most saturated thing on screen unless it is meant to draw the eye (the active unit, a badge).
- **Same scale and weight?** Is it in proportion to its neighbours (a man smaller than a knight, a town filling its cell by size)? Are line weights similar to the painting's at game size?
- **Same light?** Shadows fall the same way (down and to the right here), with the same softness.
- **Reads at 1x?** Can a player tell what it is and whose it is at game size? Too faint is a fail as well as too loud.
- **Clean at 3x?** No halos, fringes, seams, key-colour leaks (magenta, cyan), clipped edges, jaggies or blurred mush.
- **Fits the reference?** Put it next to the reference: does anything jump out as from another game? If yes, fail.

## 4. On a fail

- First look for a cause **in the pipeline** before adding anything. Often the art already has what is missing and a processing step loses it. For example, the town edge softening erased the painted walls of small walled towns. Restoring the art beats drawing over it.
- If something must be drawn, draw it in the art's language: sample colours from the art, soft and varied edges, texture or noise, painted-style shading. Then run the check again.
- Never trade style for legibility without saying so. If that trade-off cannot be avoided, ask the owner.

## 5. Report

Before saying done, include in the reply:

- the side-by-side image(s) you judged, sent to the user, and the grey sheet with `valuecheck.py`'s numbers;
- a short line per element: **pass**, or **fail** with what is wrong;
- anything not checked in the real game, said plainly.

Do not say "done", "fixed", "looks good" or "matches" for anything that has not passed this check.
