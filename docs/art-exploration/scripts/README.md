# Generator scripts (archive)

These Python scripts produced the moodboard plates. They are **not** part of the game build.

They read `OPENAI_API_KEY` from the environment. None of them embed a key. Paths inside still point at `/tmp` and `/opt/cursor/artifacts` from the session that made the images; they are kept as a record of the process, not as something to run from a fresh checkout.

`moodboard_gen.py`, `graphic_board.py`, `screen_board.py`, `painted_screens.py`, `painted_variations.py`, and `topdown_*.py` requested the early plates. `mood_blend_gen.py` requested the seamless top-down maps. `gen.py` / `gen_v2*.py` requested composite sprites; `comp.py`, `states.py`, and `v2.py` assembled sheets from those sprites.
