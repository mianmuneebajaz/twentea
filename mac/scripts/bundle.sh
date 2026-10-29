#!/bin/bash
# Builds a universal release binary and wraps it into build/Twentea.app.
# Signs ad-hoc, or with SIGN_IDENTITY (a Developer ID Application identity) when set.
set -euo pipefail

cd "$(dirname "$0")/.."
VERSION="${VERSION:-0.1.0}"
APP="build/Twentea.app"

swift build -c release --arch arm64 --arch x86_64
BIN="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)/Twentea"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/Twentea"
cp ../brand/generated/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Twentea</string>
  <key>CFBundleDisplayName</key><string>Twentea</string>
  <key>CFBundleIdentifier</key><string>app.twentea.Twentea</string>
  <key>CFBundleExecutable</key><string>Twentea</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>LSUIElement</key><true/>
  <key>NSHumanReadableCopyright</key><string>MIT License</string>
  <key>CFBundleURLTypes</key>
  <array>
    <dict>
      <key>CFBundleURLName</key><string>app.twentea.Twentea</string>
      <key>CFBundleURLSchemes</key><array><string>twentea</string></array>
    </dict>
  </array>
</dict>
</plist>
PLIST

if [ "${SIGN_IDENTITY:--}" = "-" ]; then
  codesign --force --sign - --options runtime "$APP"
else
  codesign --force --sign "$SIGN_IDENTITY" --options runtime --timestamp "$APP"
fi
echo "Built $APP ($VERSION)"
