#!/bin/bash
# Builds Chipmunk.app (Apple Silicon + Intel) and Chipmunk-mac.zip into mac/build.
# Needs Xcode or the Xcode command line tools. Run from anywhere: mac/scripts/build-app.sh
set -euo pipefail
cd "$(dirname "$0")/.."
MAC="$(pwd)"
ROOT="$(cd .. && pwd)"
VERSION="$(grep -o 'static let version = "[^"]*"' Sources/Chipmunk/Core.swift | cut -d'"' -f2)"
OUT="$MAC/build"
APP="$OUT/Chipmunk.app"

rm -rf "$OUT"
mkdir -p "$OUT"
swift build -c release --arch arm64 --arch x86_64
BIN="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)/Chipmunk"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/Chipmunk"
# The panel UI is shared with Windows; the browser mock isn't needed in the app.
cp -R "$ROOT/ui" "$APP/Contents/Resources/ui"
rm -f "$APP/Contents/Resources/ui/mock-host.js"

# App icon and menu bar icon, from the same chipmunk artwork the Windows app uses.
ICONSET="$OUT/AppIcon.iconset"
mkdir -p "$ICONSET"
SRC="$ROOT/ui/assets/chip.png"
for s in 16 32 128 256; do
  sips -z $s $s "$SRC" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  sips -z $((s*2)) $((s*2)) "$SRC" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
sips -z 36 36 "$SRC" --out "$APP/Contents/Resources/MenuIcon.png" >/dev/null
rm -rf "$ICONSET"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Chipmunk</string>
  <key>CFBundleDisplayName</key><string>Chipmunk</string>
  <key>CFBundleIdentifier</key><string>dev.bsid.chipmunk</string>
  <key>CFBundleExecutable</key><string>Chipmunk</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSHumanReadableCopyright</key><string>MIT License · bsid.dev</string>
</dict>
</plist>
PLIST

# Ad-hoc signature: required to run on Apple Silicon. Not notarized, so the first launch needs
# right-click > Open (the README explains it).
codesign --force --deep --sign - "$APP"

(cd "$OUT" && ditto -c -k --keepParent Chipmunk.app Chipmunk-mac.zip)
echo "Built $APP ($VERSION) and $OUT/Chipmunk-mac.zip"
