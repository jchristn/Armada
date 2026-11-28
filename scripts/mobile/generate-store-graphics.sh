#!/usr/bin/env bash
# Regenerate the Google Play graphics in src/Armada.Mobile/store/ on macOS:
#   play-icon-512.png     512 x 512 app icon (from assets/images/icon.png)
#   feature-graphic.png   1024 x 500, 24-bit (no alpha), rendered from store/source/feature-graphic.html
# Needs Google Chrome (headless) and sips. Screenshots are captured separately (see store/README.md).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
MOBILE="$ROOT/src/Armada.Mobile"
STORE="$MOBILE/store"
CHROME="${CHROME:-/Applications/Google Chrome.app/Contents/MacOS/Google Chrome}"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

sips -z 512 512 "$MOBILE/assets/images/icon.png" --out "$STORE/play-icon-512.png" >/dev/null

"$CHROME" --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 \
  --window-size=1024,500 --screenshot="$TMP/feature.png" "file://$STORE/source/feature-graphic.html" >/dev/null 2>&1
# Through JPEG and back drops the alpha channel, which Google Play refuses on the feature graphic.
sips -s format jpeg -s formatOptions best "$TMP/feature.png" --out "$TMP/feature.jpg" >/dev/null
sips -s format png "$TMP/feature.jpg" --out "$STORE/feature-graphic.png" >/dev/null
echo "wrote $STORE/play-icon-512.png and $STORE/feature-graphic.png"
