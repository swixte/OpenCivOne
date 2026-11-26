---
name: aesthetic-check
description: Visual quality gate for any change that affects how the game or a theme looks (art, sprites, map drawing, effects, UI). Run it before saying a visual change is done, finished, fixed or ready, and before committing one. Compares the result with the theme's reference renders, element by element, at game size and enlarged, and records a verdict for each.
---

# Aesthetic check

A visual change is not done until it passes this check. "It is visible", "it builds" and "it matches the code's intent" are not passes. The question is: **would the art owner accept this next to the reference renders?**

Drawing things in code is fine. What matters is that whatever is drawn, painted or generated sits in the same visual language as the art around it.

## 1. Gather the evidence

For every element the change touches, and every element next to it on screen:

1. **Reference:** the theme's reference renders (for gouache: `themes/gouache/preview/compose.py` output, `*-map.jpg`, `*-zoom.jpg`, or a fresh `compose.py` run). If none show the element, use the nearest element the art does show (e.g. the large walled towns for small walled ones).
2. **In the real game:** a screenshot from the running game, not only the test harness. Get the state there with a save edit if play won't reach it quickly. Note the exceptions you hit, e.g. units added by editing the save stay invisible until they move.
3. **Crops:** each element at **1x** (what a player sees) and at **3x** (nearest or Lanczos), set **side by side with the reference** in one image, then look at that image.
4. **Every case:** sizes, states and colours, e.g. every town step walled and open, every player colour, sentry/fortified/active, land and sea, start, middle and end of an animation.

## 2. Judge each element against these questions

Write the answer for each; a single "no" fails the element.

- **Same medium?** Does it look painted (soft edges, varied colour, light and shade, texture) where the art is painted? Flat fills, uniform strokes, perfect geometry, pure white or pure black lines and vector-clean shapes next to gouache are a **fail**. A drawn ring, outline or icon that does not look painted fails even if it reads well.
- **Same palette and value?** Do its colours and darkness come from the art around it (sample them), not from picked colours? Nothing should be the brightest or most saturated thing on screen unless it is meant to draw the eye (the active unit, a badge).
- **Same scale and weight?** Is it in proportion to its neighbours (a man smaller than a knight, a town filling its cell by size)? Are line weights similar to the painting's at game size?
- **Same light?** Shadows fall the same way (down and to the right here), with the same softness.
- **Reads at 1x?** Can a player tell what it is and whose it is at game size? Too faint is a fail as well as too loud.
- **Clean at 3x?** No halos, fringes, seams, key-colour leaks (magenta, cyan), clipped edges, jaggies or blurred mush.
- **Fits the reference?** Put it next to the reference: does anything jump out as from another game? If yes, fail.

## 3. On a fail

- First look for a cause **in the pipeline** before adding anything. Often the art already has what is missing and a processing step loses it. For example, the town edge softening erased the painted walls of small walled towns. Restoring the art beats drawing over it.
- If something must be drawn, draw it in the art's language: sample colours from the art, soft and varied edges, texture or noise, painted-style shading. Then run the check again.
- Never trade style for legibility without saying so. If that trade-off cannot be avoided, ask the owner.

## 4. Report

Before saying done, include in the reply:

- the side-by-side image(s) you judged, sent to the user;
- a short line per element: **pass**, or **fail** with what is wrong;
- anything not checked in the real game, said plainly.

Do not say "done", "fixed", "looks good" or "matches" for anything that has not passed this check.
