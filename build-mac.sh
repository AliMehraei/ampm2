#!/usr/bin/env bash
# Native macOS build (run on a Mac with the .NET 10 SDK: brew install --cask dotnet-sdk):
#   ./build-mac.sh                          ad-hoc signed ampm2.app + .dmg for this Mac's architecture
#   ARCH=x64 ./build-mac.sh                 Intel build
#   SIGN_ID="Developer ID Application: Your Name (TEAMID)" ./build-mac.sh
#                                           signed with hardened runtime (then notarize the .dmg with notarytool)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
ARCH="${ARCH:-$(uname -m)}"
case "$ARCH" in arm64|aarch64) RID=osx-arm64; ARCH=arm64 ;; *) RID=osx-x64; ARCH=x64 ;; esac
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/src/Ampm2.Mac/Ampm2.Mac.csproj" | head -1)"
PUB="$ROOT/publish-mac/$RID"; STAGE="$ROOT/publish-mac/$RID-app"; OUT="$ROOT/mac"
rm -rf "$PUB" "$STAGE"; mkdir -p "$STAGE" "$OUT"

dotnet publish "$ROOT/src/Ampm2.Mac/Ampm2.Mac.csproj" -c Release -r "$RID" --self-contained true -o "$PUB"
python3 "$ROOT/tools/mac-bundle.py" bundle "$PUB" "$ROOT/src/Ampm2.Mac/Assets" "$VERSION" "$STAGE"
APP="$STAGE/ampm2.app"

if [ -n "${SIGN_ID:-}" ]; then
  # .NET needs JIT and unsigned executable memory under the hardened runtime
  ENT="$STAGE/entitlements.plist"
  cat > "$ENT" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>com.apple.security.cs.allow-jit</key><true/>
  <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
  <key>com.apple.security.cs.disable-library-validation</key><true/>
</dict></plist>
PLIST
  find "$APP/Contents/MacOS" -type f \( -name '*.dylib' -o -name createdump \) -exec codesign --force --timestamp --options runtime --sign "$SIGN_ID" {} \;
  codesign --force --timestamp --options runtime --entitlements "$ENT" --sign "$SIGN_ID" "$APP"
else
  codesign --force --deep --sign - "$APP"
fi
codesign --verify --deep --strict "$APP" && echo "signature ok"

DMG="$OUT/ampm2-$VERSION-macos-$ARCH.dmg"
rm -f "$DMG"
DMGDIR="$STAGE/dmg"; mkdir -p "$DMGDIR"; cp -R "$APP" "$DMGDIR/"; ln -s /Applications "$DMGDIR/Applications"
hdiutil create -volname "ampm2 $VERSION" -srcfolder "$DMGDIR" -ov -format UDZO "$DMG" >/dev/null
echo "Built $DMG"
[ -n "${SIGN_ID:-}" ] && echo "Notarize: xcrun notarytool submit \"$DMG\" --keychain-profile <profile> --wait && xcrun stapler staple \"$DMG\""
