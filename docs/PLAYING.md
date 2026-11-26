# Playing OpenCivOne with the new art

This runs the game on your own computer (Windows, macOS or Linux) with the ukiyo-e woodblock theme: the isometric
map, the painted towns by nation and age, the palace and garden, and the rest of the redrawn screens.

## What you need

- **.NET 8 SDK**: <https://dotnet.microsoft.com/download/dotnet/8.0>
- **Python 3** with `numpy` and `Pillow` (`pip install numpy pillow`), to set up the game data and the theme
- **Git**, to get the code

## 1. Get the code

```
git clone https://github.com/swixte/OpenCivOne.git
cd OpenCivOne
git checkout hd-fallback-layer
```

## 2. Game data

The game reads its data from a folder named `Dos/Civ1`:

- Windows: `C:\Dos\Civ1`
- macOS and Linux: `~/Dos/Civ1` (in your home folder)

Use **one** of these:

- **Your own copy of the original Civilization (DOS)**: copy its `.PIC`, `.PAL` and `.TXT` files into that folder.
- **The copyright-free stand-in data** from this repository (no original files needed):

  ```
  sh tools/standin/build.sh
  ```

  On Windows, run that from Git Bash. It writes the stand-in data set into `~/Dos/Civ1` (pass another folder as
  its argument if you want it elsewhere, then copy it to `C:\Dos\Civ1`).

## 3. Install the theme

```
cd tools/hdart
python3 theme.py install ../../themes/ukiyo-e
cd ../..
```

This copies the art into `Dos/Civ1/themes/ukiyo-e`. On Windows add the game folder as a second argument:
`python3 theme.py install ../../themes/ukiyo-e C:\Dos\Civ1`.

## 4. Turn the theme on

Edit (or create) the preferences file:

- Windows: `%USERPROFILE%\Games\OpenCivOne\Preferences.json`
- macOS and Linux: `~/Games/OpenCivOne/Preferences.json`

so that it contains:

```json
{ "Theme": "ukiyo-e" }
```

## 5. Play

```
dotnet run --project src
```

The first build takes a minute. Choose **Start a New Game**, pick your civilization and world, and play. The map
is the isometric woodblock view; cities show their nation's style, grow through four sizes, show walls once
they build City Walls, and change from ancient to medieval, industrial and modern as you discover Monarchy or
Feudalism, Industrialization, and The Corporation or Electricity.

## Settings worth knowing

- **Bigger worlds**: the land mass menu at the start of a game has a new **Huge** option.
- **Smoother terrain** (optional, off by default): add `"TerrainSmoothing": true` to the preferences file for
  continents with broad, natural terrain regions instead of the original scattered tiles.
- **Debug mode** (`"Debug": { "DebugMode": true }`) shows the whole map and every civilization; leave it off for
  a normal game.

## If something goes wrong

- *The game says files are missing*: the data folder is in the wrong place or step 2 was skipped.
- *The map looks like the original flat tiles*: the theme is not installed (step 3) or not turned on (step 4).
- A crash prints `GAME EXCEPTION` with details in the console; please send those lines along.

## Sound

The game plays its music and effects through OpenAL, which comes with it, so nothing extra is needed. Battles,
city events, audiences, nuclear blasts and the rest have sounds made by the game itself. Sound follows the
game's own **Sound** option in the game menu. A theme can use its own recordings instead: put 16 bit PCM WAV files named
`tuneNN.wav` (for example `tune38.wav`, a battle won) in the theme's `sounds` folder; the numbers are listed at
the top of `src/Sound/Synth.cs`.
