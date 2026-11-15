#!/bin/sh
# Generates the v6b concept sheets: the v6 picks redrawn with stronger print linework, the busy city, and the science
# screens as three landscape sheets. Run from the repository root:
#   OPENAI_API_KEY=... sh docs/art-exploration/ukiyo-v6b/run.sh [SHEET ...]
# SHEET is city, city-busy, science-choose, science-report, science-discover, frontend or kit (default: all).
# TAKES sets takes per sheet (default 2). A greyscale copy is written beside each take.
set -e
V6B=docs/art-exploration/ukiyo-v6b
V6=docs/art-exploration/ukiyo-v6
REFS=themes/ukiyo-e/refs
TAKES=${TAKES:-2}
SHEETS=${*:-city city-busy science-choose science-report science-discover frontend kit}

# the first reference is the v6 pick each prompt builds on; Hokusai's print sets the linework
refs_for() {
	case $1 in
		city) echo "$V6B/city-pick.jpg $REFS/landscape-hokusai.jpg $REFS/v5-screen.jpg" ;;
		city-busy) echo "$V6B/city-pick.jpg $REFS/landscape-hokusai.jpg $V6/kit-1.jpg" ;;
		science-choose) echo "$V6/kit-1.jpg $V6/science-1.jpg $REFS/v5-screen.jpg" ;;
		science-report) echo "$V6/science-1.jpg $V6/kit-1.jpg $REFS/landscape-hokusai.jpg" ;;
		science-discover) echo "$V6/science-1.jpg $REFS/landscape-hiroshige.jpg $REFS/landscape-hokusai.jpg" ;;
		frontend) echo "$V6/frontend-2.jpg $V6/kit-1.jpg $REFS/landscape-hokusai.jpg" ;;
		kit) echo "$V6/kit-1.jpg $REFS/landscape-hokusai.jpg" ;;
	esac
}

for s in $SHEETS; do
	prompt=$(mktemp)
	cat "$V6B/prompt-$s.txt" "$V6B/style.txt" > "$prompt"
	for t in $(seq 1 "$TAKES"); do
		out="$V6B/$s-$t.png"
		[ -f "$out" ] && { echo "$out exists, skipped"; continue; }
		python3 docs/art-exploration/scripts/concept_v4.py "$out" "$prompt" $(refs_for "$s")
		python3 -c "from PIL import Image; import sys; Image.open(sys.argv[1]).convert('L').save(sys.argv[2], quality=85)" "$out" "$V6B/$s-$t-grey.jpg"
	done
	rm -f "$prompt"
done
