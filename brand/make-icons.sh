#!/bin/bash
# Renders brand/twentea-icon.svg into brand/generated/AppIcon.icns (macOS) and twentea.ico (Windows).
# Needs macOS (Quick Look, sips, iconutil). The outputs are committed so CI never renders SVG.
set -euo pipefail

cd "$(dirname "$0")"
mkdir -p generated
TMP="$(mktemp -d)"
qlmanage -t -s 1024 -o "$TMP" twentea-icon.svg >/dev/null 2>&1
SRC="$TMP/twentea-icon.svg.png"

SET="$TMP/AppIcon.iconset"
mkdir -p "$SET"
for size in 16 32 128 256 512; do
  sips -z $size $size "$SRC" --out "$SET/icon_${size}x${size}.png" >/dev/null
  sips -z $((size * 2)) $((size * 2)) "$SRC" --out "$SET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$SET" -o generated/AppIcon.icns

swift make-ico.swift "$SRC" generated/twentea.ico
rm -rf "$TMP"
echo "Wrote brand/generated/AppIcon.icns and brand/generated/twentea.ico"
