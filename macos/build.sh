#!/bin/bash
# Builds "Alpha Branding Studio.app" (universal: Apple silicon + Intel) and a release zip into macos/build.
# Needs only the Xcode Command Line Tools (xcode-select --install).
#
#   ./build.sh            version taken from src/Alpha.Branding/Alpha.Branding.csproj
#   ./build.sh 2.5.0      explicit version (the release workflow passes the tag's version)
set -euo pipefail
cd "$(dirname "$0")"

ASSETS="../src/Alpha.Branding/Assets"
VERSION="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' ../src/Alpha.Branding/Alpha.Branding.csproj | head -1)}"
if [[ -z "$VERSION" ]]; then
  echo "Could not determine the version; pass it as the first argument." >&2
  exit 1
fi

APP="build/Alpha Branding Studio.app"
OBJ="build/obj"
ZIP="build/Alpha.Branding-v$VERSION-macos.zip"
rm -rf build
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$OBJ"

for arch in arm64 x86_64; do
  echo "Compiling for ${arch}..."
  swiftc -O -swift-version 5 -parse-as-library \
    -target "$arch-apple-macos13.0" \
    Sources/*.swift -o "$OBJ/AlphaBranding-$arch"
done
lipo -create "$OBJ"/AlphaBranding-* -output "$APP/Contents/MacOS/AlphaBranding"

cp Info.plist "$APP/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" -c "Set :CFBundleVersion $VERSION" \
  "$APP/Contents/Info.plist"
cp "$ASSETS/alpha_branding.png" "$ASSETS/logo_phoenix.png" "$APP/Contents/Resources/"

ICONSET="$OBJ/AppIcon.iconset"
mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
  sips -z $size $size "$ASSETS/app.png" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  sips -z $((size * 2)) $((size * 2)) "$ASSETS/app.png" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

# Ad-hoc signature. Without an Apple Developer ID, other Macs ask the user to confirm the first launch.
codesign --force --sign - "$APP"
ditto -c -k --norsrc --noextattr --keepParent "$APP" "$ZIP"
echo "Built $APP (v$VERSION) and $ZIP"
