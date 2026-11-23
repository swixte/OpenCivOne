#!/bin/sh
# Generates the v6 concept sheets. Run from the repository root with this folder at docs/art-exploration/ukiyo-v6:
#   OPENAI_API_KEY=... sh docs/art-exploration/ukiyo-v6/run.sh [SHEET ...]
# SHEET is city, city-busy, science, frontend or kit (default: all). TAKES sets takes per sheet (default 3).
# Each take is one medium-quality 1536x1024 image; a greyscale copy is written beside it to check contrast, as in v5.
set -e
V6=docs/art-exploration/ukiyo-v6
V4=docs/art-exploration/ukiyo-v4
REFS=themes/ukiyo-e/refs
TAKES=${TAKES:-3}
SHEETS=${*:-city city-busy science frontend kit}

refs_for() {
	case $1 in
		# the city sheets keep v4's layout language and v5's map look
		city) echo "$V4/cityscreen.jpg $REFS/v5-screen.jpg $V4/ui.jpg" ;;
		# the busy city follows the first city take that was picked (copy it to city-pick.jpg)
		city-busy) echo "$V6/city-pick.jpg $V4/cityscreen.jpg $V4/ui.jpg" ;;
		science) echo "$V4/ui.jpg $V4/scenes.jpg $V4/advisors.jpg" ;;
		frontend) echo "$V4/ui.jpg $V4/backdrops.jpg $REFS/landscape-hokusai.jpg" ;;
		kit) echo "$V4/ui.jpg $V4/cityscreen.jpg" ;;
	esac
}

for s in $SHEETS; do
	if [ "$s" = city-busy ] && [ ! -f "$V6/city-pick.jpg" ]; then
		echo "city-busy: pick a city take first and copy it to $V6/city-pick.jpg"; continue
	fi
	prompt=$(mktemp)
	cat "$V6/prompt-$s.txt" "$V6/style.txt" > "$prompt"
	for t in $(seq 1 "$TAKES"); do
		out="$V6/$s-$t.png"
		[ -f "$out" ] && { echo "$out exists, skipped"; continue; }
		python3 docs/art-exploration/scripts/concept_v4.py "$out" "$prompt" $(refs_for "$s")
		python3 -c "from PIL import Image; import sys; Image.open(sys.argv[1]).convert('L').save(sys.argv[2], quality=85)" "$out" "$V6/$s-$t-grey.jpg"
	done
	rm -f "$prompt"
done
