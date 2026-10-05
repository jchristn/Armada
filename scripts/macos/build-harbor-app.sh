#!/usr/bin/env bash
# =====================================================================
# build-harbor-app.sh -- build "Armada Harbor.app" in a .dmg for each macOS
# runtime (osx-arm64, osx-x64) with Armada.Publisher (channel dmg-harbor),
# then verify what was built (V1 readiness W5.3):
#   - the disk image checksums (hdiutil verify) and its drag-to-install
#     layout: "Armada Harbor.app" next to an "Applications" link;
#   - Info.plist: bundle id, version, executable, icon, LSUIElement (menu
#     bar app), LSMultipleInstancesProhibited, minimum macOS; plutil -lint;
#   - Contents/Resources/AppIcon.icns is a real icns;
#   - the code signature (codesign --verify --deep --strict) and whether it
#     is ad-hoc or Developer ID, plus notarization staple and Gatekeeper
#     assessment (reported; only enforced with --require-notarized);
#   - for the host architecture: the bundled binary runs and
#     "--install-startup --dry-run" writes nothing and names the LaunchAgent
#     (HOME is a temp directory, so nothing touches the real login items).
#
# Signing: with no Apple credentials in the environment the app is ad-hoc
# signed (fine for local testing on this Mac). For Developer ID signing and
# notarization set the variables listed in BUILDING_INSTALLERS.md
# ("macOS signing and notarization") before running.
#
# Usage: scripts/macos/build-harbor-app.sh [--version X.Y.Z] [--output DIR]
#                                          [--no-build] [--require-notarized]
#   --version            default: <Version> in src/Directory.Build.props
#   --output             default: installers/<version>
#   --no-build           verify the .dmg files already in the output dir
#   --require-notarized  fail unless each image is Developer ID signed,
#                        notarized, stapled, and accepted by Gatekeeper
# =====================================================================
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VERSION=""
OUTPUT=""
BUILD=1
REQUIRE_NOTARIZED=0
while [ $# -gt 0 ]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    --output) OUTPUT="$2"; shift 2 ;;
    --no-build) BUILD=0; shift ;;
    --require-notarized) REQUIRE_NOTARIZED=1; shift ;;
    -h|--help) grep '^#' "$0" | grep -v '^#!' | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

[ "$(uname -s)" = "Darwin" ] || { echo "build-harbor-app.sh runs on macOS only" >&2; exit 2; }
if [ -z "$VERSION" ]; then
  VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$REPO_ROOT/src/Directory.Build.props" | head -1)"
fi
[ -n "$VERSION" ] || { echo "could not determine the version; pass --version" >&2; exit 2; }
OUTPUT="${OUTPUT:-$REPO_ROOT/installers/$VERSION}"
mkdir -p "$OUTPUT"
OUTPUT="$(cd "$OUTPUT" && pwd)"

FAILURES=0
pass() { printf 'PASS  %s\n' "$*"; }
fail() { printf 'FAIL  %s\n' "$*"; FAILURES=$((FAILURES + 1)); }
info() { printf '      %s\n' "$*"; }
check() { local label="$1"; shift; if "$@" >/dev/null 2>&1; then pass "$label"; else fail "$label"; fi; }
plist_get() { /usr/libexec/PlistBuddy -c "Print :$2" "$1" 2>/dev/null; }
expect_key() {
  local plist="$1" key="$2" want="$3" got
  got="$(plist_get "$plist" "$key")"
  if [ "$got" = "$want" ]; then pass "Info.plist $key = $want"; else fail "Info.plist $key = '$got' (expected '$want')"; fi
}

if [ "$BUILD" -eq 1 ]; then
  WORK="$OUTPUT/_work"
  mkdir -p "$WORK"
  echo "[harbor-app] building dmg-harbor $VERSION into $OUTPUT"
  dotnet build "$REPO_ROOT/src/Armada.Publisher/Armada.Publisher.csproj" -c Release --nologo -v quiet || exit 1
  (cd "$REPO_ROOT" && dotnet run --project src/Armada.Publisher -c Release --no-build -- \
     --channel dmg-harbor --version "$VERSION" --output "$WORK") || { echo "[harbor-app] publisher failed" >&2; exit 1; }
  find "$WORK/packages/dmg-harbor" -maxdepth 1 -name "*.dmg" -exec cp {} "$OUTPUT"/ \;
  find "$WORK/packages/dmg-harbor" -maxdepth 1 -name "*.dmg.sha256" -exec cp {} "$OUTPUT"/ \;
fi

HOST_ARCH="$(uname -m)"
case "$HOST_ARCH" in arm64) HOST_RID="osx-arm64" ;; *) HOST_RID="osx-x64" ;; esac

shopt -s nullglob
IMAGES=("$OUTPUT"/armada-harbor-"$VERSION"-osx-*.dmg)
[ "${#IMAGES[@]}" -gt 0 ] || { echo "no armada-harbor-$VERSION-osx-*.dmg in $OUTPUT" >&2; exit 1; }

for DMG in "${IMAGES[@]}"; do
  RID="$(basename "$DMG" .dmg)"; RID="${RID##*-osx-}"; RID="osx-$RID"
  echo
  echo "=== $(basename "$DMG") ($RID) ==="
  check "hdiutil verify" hdiutil verify -quiet "$DMG"
  if [ -f "$DMG.sha256" ]; then
    want="$(awk '{print $1}' "$DMG.sha256")"; got="$(shasum -a 256 "$DMG" | awk '{print $1}')"
    if [ "$want" = "$got" ]; then pass "sha256 sidecar matches"; else fail "sha256 sidecar does not match"; fi
  fi

  MOUNT="$(mktemp -d "${TMPDIR:-/tmp}/armada-harbor-dmg.XXXXXX")"
  if ! hdiutil attach -quiet -readonly -nobrowse -noautoopen -mountpoint "$MOUNT" "$DMG"; then
    fail "mount $DMG"; rmdir "$MOUNT" 2>/dev/null; continue
  fi

  APP="$MOUNT/Armada Harbor.app"
  PLIST="$APP/Contents/Info.plist"
  check "layout: Armada Harbor.app" test -d "$APP"
  if [ -L "$MOUNT/Applications" ] && [ "$(readlink "$MOUNT/Applications")" = "/Applications" ]; then
    pass "layout: Applications -> /Applications"
  else
    fail "layout: Applications link missing"
  fi
  check "plutil -lint Info.plist" plutil -lint "$PLIST"
  expect_key "$PLIST" CFBundleIdentifier "com.joelchristner.armada.harbor"
  expect_key "$PLIST" CFBundleName "Armada Harbor"
  expect_key "$PLIST" CFBundlePackageType "APPL"
  expect_key "$PLIST" CFBundleShortVersionString "$VERSION"
  expect_key "$PLIST" CFBundleIconFile "AppIcon"
  expect_key "$PLIST" LSUIElement "true"
  expect_key "$PLIST" LSMultipleInstancesProhibited "true"
  expect_key "$PLIST" LSMinimumSystemVersion "14.0"
  expect_key "$PLIST" NSHighResolutionCapable "true"
  EXE="$(plist_get "$PLIST" CFBundleExecutable)"
  check "executable Contents/MacOS/$EXE" test -x "$APP/Contents/MacOS/$EXE"
  if file "$APP/Contents/Resources/AppIcon.icns" 2>/dev/null | grep -q "Mac OS X icon"; then
    pass "AppIcon.icns is an icns"
  else
    fail "AppIcon.icns missing or not an icns"
  fi
  check "PkgInfo" test -f "$APP/Contents/PkgInfo"

  check "codesign --verify --deep --strict" codesign --verify --deep --strict "$APP"
  SIGNATURE="$(codesign -dv "$APP" 2>&1)"
  if printf '%s\n' "$SIGNATURE" | grep -q "Signature=adhoc"; then
    info "signature: ad-hoc (no Developer ID; runs on this Mac, Gatekeeper blocks it elsewhere)"
    SIGNED_DEVID=0
  else
    info "signature: $(printf '%s\n' "$SIGNATURE" | grep -m1 '^Authority=' || echo unknown)"
    SIGNED_DEVID=1
    if printf '%s\n' "$SIGNATURE" | grep -q "flags=.*runtime"; then pass "hardened runtime"; else fail "hardened runtime not enabled"; fi
  fi
  if xcrun stapler validate -q "$DMG" >/dev/null 2>&1; then STAPLED=1; info "notarization ticket stapled to the .dmg"; else STAPLED=0; info "no notarization ticket stapled"; fi
  if spctl -a -t open --context context:primary-signature "$DMG" >/dev/null 2>&1; then GATEKEEPER=1; info "Gatekeeper accepts the .dmg"; else GATEKEEPER=0; info "Gatekeeper rejects the .dmg (expected when not notarized)"; fi
  if [ "$REQUIRE_NOTARIZED" -eq 1 ]; then
    if [ "$SIGNED_DEVID" -eq 1 ] && [ "$STAPLED" -eq 1 ] && [ "$GATEKEEPER" -eq 1 ]; then pass "Developer ID signed, notarized, stapled"; else fail "not Developer ID signed, notarized, and stapled"; fi
  fi

  if [ "$RID" = "$HOST_RID" ]; then
    FAKE_HOME="$(mktemp -d "${TMPDIR:-/tmp}/armada-harbor-home.XXXXXX")"
    OUT="$(HOME="$FAKE_HOME" "$APP/Contents/MacOS/$EXE" --install-startup --dry-run 2>&1)"; RC=$?
    if [ "$RC" -eq 0 ] && printf '%s\n' "$OUT" | grep -q "com.joelchristner.armada.harbor" \
       && printf '%s\n' "$OUT" | grep -q "Contents/MacOS/$EXE" && printf '%s\n' "$OUT" | grep -q -- "--minimized"; then
      pass "--install-startup --dry-run names the LaunchAgent, the bundled binary, and --minimized"
    else
      fail "--install-startup --dry-run (exit $RC)"; printf '%s\n' "$OUT" | sed 's/^/      /' | tail -20
    fi
    if [ -e "$FAKE_HOME/Library/LaunchAgents" ]; then fail "--dry-run wrote to LaunchAgents"; else pass "--dry-run wrote nothing"; fi
    rm -rf "$FAKE_HOME"
  else
    info "skipping the binary checks: $RID does not run natively on $HOST_ARCH"
  fi

  hdiutil detach -quiet "$MOUNT" >/dev/null 2>&1 || hdiutil detach -force -quiet "$MOUNT" >/dev/null 2>&1
  rmdir "$MOUNT" 2>/dev/null || true
done

echo
if [ "$FAILURES" -eq 0 ]; then
  echo "RESULT: PASS (${#IMAGES[@]} image(s) in $OUTPUT)"
  exit 0
fi
echo "RESULT: FAIL ($FAILURES check(s) failed)"
exit 1
