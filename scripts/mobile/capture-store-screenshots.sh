#!/usr/bin/env bash
# Capture the store screenshots listed in src/Armada.Mobile/store/README.md from a running Release build.
#
# Before running: boot the simulator or emulator for the target (see the README for device and size), install a
# Release build of the app (npx expo run:ios --configuration Release, or npx expo run:android --variant release),
# sign in to the demo Admiral (RS-16.9a) so every screen has representative data, and set the ids below.
#
#   scripts/mobile/capture-store-screenshots.sh ios-6.9|ipad-13|android-phone
#
# Environment:
#   MISSION_ID   a mission with several attempts (escalation and repair) for the attempts shot
#   VOYAGE_ID    a voyage with a mixed open and frontier pipeline
#   PROFILE_ID   a model profile with a scorecard (an open model)
#   SETTLE       seconds to wait after each navigation (default 4)
#
# Each shot opens a deep link (armada://<path>), waits, and saves a PNG without alpha, as both stores require.
set -euo pipefail
TARGET="${1:?usage: capture-store-screenshots.sh ios-6.9|ipad-13|android-phone}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/src/Armada.Mobile/store/$TARGET"
SETTLE="${SETTLE:-4}"
: "${MISSION_ID:?set MISSION_ID}"
: "${VOYAGE_ID:?set VOYAGE_ID}"
: "${PROFILE_ID:?set PROFILE_ID}"
mkdir -p "$OUT"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

case "$TARGET" in
  ios-6.9|ipad-13) PLATFORM=ios ;;
  android-phone) PLATFORM=android ;;
  *) echo "unknown target $TARGET" >&2; exit 2 ;;
esac

if [ "$PLATFORM" = ios ]; then
  xcrun simctl status_bar booted override --time 9:41 --batteryState charged --batteryLevel 100 \
    --cellularMode active --cellularBars 4 --wifiBars 3
else
  adb shell settings put global sysui_demo_allowed 1
  adb shell am broadcast -a com.android.systemui.demo -e command enter >/dev/null
  adb shell am broadcast -a com.android.systemui.demo -e command clock -e hhmm 0941 >/dev/null
  adb shell am broadcast -a com.android.systemui.demo -e command battery -e level 100 -e plugged false >/dev/null
fi

open_link() {
  if [ "$PLATFORM" = ios ]; then
    xcrun simctl openurl booted "armada://$1"
  else
    adb shell am start -W -a android.intent.action.VIEW -d "armada://$1" >/dev/null
  fi
  sleep "$SETTLE"
}

set_appearance() {
  if [ "$PLATFORM" = ios ]; then
    xcrun simctl ui booted appearance "$1"
  else
    if [ "$1" = dark ]; then adb shell cmd uimode night yes; else adb shell cmd uimode night no; fi
  fi
  sleep 2
}

shot() {
  if [ "$PLATFORM" = ios ]; then
    xcrun simctl io booted screenshot "$TMP/raw.png" >/dev/null
  else
    adb exec-out screencap -p > "$TMP/raw.png"
  fi
  # Through JPEG and back drops the alpha channel.
  sips -s format jpeg -s formatOptions best "$TMP/raw.png" --out "$TMP/raw.jpg" >/dev/null
  sips -s format png "$TMP/raw.jpg" --out "$OUT/$1.png" >/dev/null
  echo "saved $OUT/$1.png"
}

set_appearance light
open_link "home";                          shot 01-home
open_link "missions?tab=voyages";          shot 02-voyages
open_link "voyages/$VOYAGE_ID";            shot 03-voyage-pipeline
open_link "missions/$MISSION_ID";          shot 04-mission-attempts
open_link "captains?tab=profiles";         shot 05-model-profiles
open_link "model-profiles/$PROFILE_ID";    shot 06-model-profile
open_link "approvals";                     shot 07-approvals
set_appearance dark
open_link "missions/$MISSION_ID";          shot 08-dark-mission
set_appearance light

if [ "$PLATFORM" = ios ]; then
  xcrun simctl status_bar booted clear
else
  adb shell am broadcast -a com.android.systemui.demo -e command exit >/dev/null
fi
