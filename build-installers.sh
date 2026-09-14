#!/usr/bin/env bash
# =============================================================================
#  build-installers.sh - build the installers this OS can produce (CLI, Harbor,
#  Server) and land them in installers/<version>/.
#
#  On macOS this builds .dmg / .pkg (and the Homebrew cask); on Linux it builds
#  .deb / .rpm / AppImage. Windows installers (.exe/.msi) cannot be built here;
#  run build-installers.bat on Windows, or push a tag to let the CI matrix build
#  all three operating systems at once.
#
#  Usage:   ./build-installers.sh <version>
#  Example: ./build-installers.sh 0.9.0
# =============================================================================
set -u
cd "$(dirname "$0")"

VERSION="${1:-}"
if [ -z "$VERSION" ]; then
  echo "Usage: ./build-installers.sh <version>"
  echo "Example: ./build-installers.sh 0.9.0"
  exit 1
fi

PUBLISHER="src/Armada.Publisher/Armada.Publisher.csproj"
OUT="installers/$VERSION"
WORK="$OUT/_work"
mkdir -p "$OUT" "$WORK"

echo "Building Armada.Publisher..."
if ! dotnet build "$PUBLISHER" -c Release --nologo; then
  exit 1
fi

BUILT=""
FAILED=""

# Build one channel; record pass/fail and keep going so one missing tool or
# unimplemented recipe does not abort the whole run.
build() {
  ch="$1"
  echo
  echo "--- channel $ch ---"
  if dotnet run --project "$PUBLISHER" -c Release --no-build -- \
       --channel "$ch" --version "$VERSION" --output "$WORK"; then
    BUILT="$BUILT $ch"
  else
    FAILED="$FAILED $ch"
  fi
}

# --- channels this OS owns ---------------------------------------------------
OS="$(uname -s)"
case "$OS" in
  Darwin)
    build dmg-harbor
    build pkg-server
    build brew-harbor
    ;;
  Linux)
    build linux-cli
    build linux-harbor
    build linux-server
    build appimage-cli
    ;;
  *)
    echo "Unsupported OS for this script: $OS"
    echo "Use build-installers.bat on Windows."
    exit 1
    ;;
esac
build nuget-cli

# --- collect the finished installers into installers/<version>/ --------------
echo
echo "Collecting installers into $OUT ..."
if [ -d "$WORK/packages" ]; then
  find "$WORK/packages" -type f \
    \( -name '*.dmg' -o -name '*.pkg' -o -name '*.deb' -o -name '*.rpm' \
       -o -name '*.AppImage' -o -name '*.nupkg' \) \
    -exec cp {} "$OUT"/ \;
fi

echo
echo "============================================================"
echo "  Version: $VERSION"
echo "  Built: $BUILT"
echo "  Failed:$FAILED"
echo "  Output: $OUT"
echo "============================================================"
if [ -n "$FAILED" ]; then
  echo "Note: failed channels are either missing a packaging tool (run"
  echo "      \"dotnet run --project $PUBLISHER -- doctor\") or their recipe is"
  echo "      not yet implemented in Armada.Publisher."
fi
