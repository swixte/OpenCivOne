#!/bin/sh
# Builds the stand-in data set into the folder given (default: ~/Dos/Civ1, where a debug build looks for game files),
# with the HD map art and the painted map art in its hd folder.
set -e
HERE=$(cd "$(dirname "$0")" && pwd)
OUT=${1:-$HOME/Dos/Civ1}
mkdir -p "$OUT"
python3 "$HERE/make_standin.py" "$OUT"
dotnet run --project "$HERE/PicWriter" -- "$OUT"
rm -rf "$OUT/raw" "$OUT/palette.bin"
rm -rf "$OUT/hd"
# HD_SET=hd4 installs the 4x sprite set that goes with the painted map instead of the 2x set
cp -r "$HERE/../../assets/${HD_SET:-hd}" "$OUT/hd"
cp -r "$HERE/../../assets/painted" "$OUT/hd/painted"
echo "Stand-in data set ready in $OUT"
