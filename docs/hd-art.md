# HD art

The game can show 2x art in place of the original 320x200 graphics. Nothing changes unless HD images are present, so the default look stays the original one.

## How it works

Every PIC file the game loads (terrain sheet `TER257.PIC`, unit sheet `SP257.PIC`, full screen pictures and so on) can have an HD twin:

```
<game resource folder>/hd/TER257.png
```

The PNG must be exactly twice the size of the PIC (a 320x200 PIC needs a 640x400 PNG). Sprites cut from a sheet carry their part of the HD image with them, so when the game draws a terrain tile or a unit from that sheet, the HD pixels are drawn. Anything drawn without HD art, such as text, menus, boxes and lines, falls back to the original pixels, upscaled with nearest-neighbour.

- Transparent PNG pixels are transparent when the sprite is drawn, and partly transparent pixels are blended.
- HD art can draw where the original sprite is transparent, as long as it stays inside the sprite's original rectangle.
- A PNG with the wrong size is ignored.

## Starting point for artists

Set `"ExportHDTemplates": true` in the `Debug` section of `Preferences.json` (in `~/Games/OpenCivOne`) and play for a while. Every PIC the game loads is written to `~/Games/OpenCivOne/hd-templates` as a 2x nearest-neighbour PNG. Index 0 is written as transparent.

Copying a template into the `hd` folder unchanged gives exactly the original look. Repaint it tile by tile; parts you have not repainted keep looking like the original.

## Known limits

- Palette effects, such as fades and colour cycling, change only original pixels. HD pixels keep their colours.
- Sentried units are greyed by swapping palette colours, so a sentried unit with HD art shows its original pixels where colours are swapped.
- Sprites cannot yet draw outside their 16x16 tile, and there are no drop shadows. Both need changes to how the map is drawn.
