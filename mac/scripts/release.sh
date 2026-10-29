#!/bin/bash
# Builds dist/Twentea-$VERSION.dmg and dist/Twentea-$VERSION-mac.zip.
# With SIGN_IDENTITY and the APPLE_* variables set, it also signs and notarizes them.
set -euo pipefail
cd "$(dirname "$0")/.."
: "${VERSION:?set VERSION, for example VERSION=1.2.3}"

scripts/bundle.sh
mkdir -p dist
DMG="dist/Twentea-$VERSION.dmg"
ZIP="dist/Twentea-$VERSION-mac.zip"
rm -f "$DMG" "$ZIP"

STAGE="$(mktemp -d)"
cp -R build/Twentea.app "$STAGE/"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname Twentea -srcfolder "$STAGE" -ov -format UDZO "$DMG" >/dev/null
rm -rf "$STAGE"

if [ "${SIGN_IDENTITY:--}" != "-" ] && [ -n "${APPLE_ID:-}" ]; then
  codesign --force --sign "$SIGN_IDENTITY" --timestamp "$DMG"
  xcrun notarytool submit "$DMG" --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" \
    --password "$APPLE_APP_PASSWORD" --wait
  xcrun stapler staple "$DMG"
  # The DMG submission covers the app inside it, so the app can carry the same ticket in the zip.
  xcrun stapler staple build/Twentea.app
  echo "Signed and notarized"
else
  echo "Unsigned build (ad-hoc). Set SIGN_IDENTITY and APPLE_* to sign and notarize."
fi

ditto -c -k --keepParent build/Twentea.app "$ZIP"
echo "Wrote $DMG and $ZIP"
