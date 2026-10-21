#!/bin/sh
# Regenerate the SVGs and render them to PNGs Unity loads from Resources/BattleHUD.
# Run from the repo root: sh ArtSource/BattleHUD/render.sh
set -e
SRC=ArtSource/BattleHUD
DST=Assets/Resources/BattleHUD
TMP=$(mktemp -d)
CHROME="/c/Program Files/Google/Chrome/Application/chrome.exe"
python "$SRC/gen_hud.py" "$SRC"
mkdir -p "$DST"
for s in "$SRC"/*.svg; do
  n=$(basename "$s" .svg)
  w=$(grep -o 'width="[0-9]*"' "$s" | head -1 | grep -o '[0-9]*')
  h=$(grep -o 'height="[0-9]*"' "$s" | head -1 | grep -o '[0-9]*')
  printf '<html><body style="margin:0;background:transparent;overflow:hidden">%s</body></html>' "$(cat "$s")" > "$TMP/$n.html"
  "$CHROME" --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 \
    --default-background-color=00000000 --window-size=$w,$h \
    --screenshot="$(cygpath -w "$PWD/$DST/$n.png")" "file:///$(cygpath -m "$TMP/$n.html")" 2>/dev/null
done
rm -rf "$TMP"
echo "rendered to $DST"
