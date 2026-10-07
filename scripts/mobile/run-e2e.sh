#!/usr/bin/env bash
# Mobile end-to-end tests (Maestro) against a throwaway Admiral.
#
# What it does, reproducibly:
#   1. builds Armada.Server (Release) from this checkout unless --no-server-build;
#   2. starts a throwaway Admiral on 127.0.0.1 with ARMADA_DATA_DIR in a temp directory (never ~/.armada) and
#      ports from --port (default 44010; MCP is port+1; Prometheus is off). It keeps the default admin password,
#      which the Admiral allows on loopback, so the flows exercise the "Skip for now" password-change path;
#   3. builds and installs a Release build of the app (JS bundle embedded, no Metro) unless --no-app-build;
#   4. runs every flow in src/Armada.Mobile/e2e with SERVER_URL pointing at the Admiral as the device sees it
#      (127.0.0.1 from the iOS simulator, 10.0.2.2 from the Android emulator);
#   5. stops the Admiral, and shuts down any simulator or emulator it booted.
#
# Usage:
#   scripts/mobile/run-e2e.sh --platform ios [--device "iPhone 17"] [--port 44010] [--no-app-build] [--no-server-build]
#   scripts/mobile/run-e2e.sh --platform android [--avd Armada_Phone] [--port 44010] [--no-app-build]
#   scripts/mobile/run-e2e.sh --platform both
# Options:
#   --keep            leave the Admiral, simulator, and emulator running (prints how to stop them)
#   --output DIR      Maestro reports and screenshots (default: a temp directory, printed at the end)
#   --flows PATH      a single flow file or folder (default: src/Armada.Mobile/e2e)
#
# Requirements: .NET 10 SDK, Node 24, Xcode + CocoaPods (iOS), Android SDK + JDK 17 (Android), Maestro
# (curl -fsSL https://get.maestro.mobile.dev | bash). See src/Armada.Mobile/README.md.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
MOBILE="${REPO_ROOT}/src/Armada.Mobile"
FRAMEWORK="net10.0"

PLATFORM=""
IOS_DEVICE="iPhone 17"
AVD="Armada_Phone"
PORT=44010
BUILD_APP=1
BUILD_SERVER=1
KEEP=0
OUTPUT=""
FLOWS="${MOBILE}/e2e"
APP_ID="${ARMADA_MOBILE_BUNDLE_ID:-com.armada.mobile}"

while [ $# -gt 0 ]; do
  case "$1" in
    --platform) PLATFORM="$2"; shift 2 ;;
    --device) IOS_DEVICE="$2"; shift 2 ;;
    --avd) AVD="$2"; shift 2 ;;
    --port) PORT="$2"; shift 2 ;;
    --no-app-build) BUILD_APP=0; shift ;;
    --no-server-build) BUILD_SERVER=0; shift ;;
    --keep) KEEP=1; shift ;;
    --output) OUTPUT="$2"; shift 2 ;;
    --flows) FLOWS="$2"; shift 2 ;;
    -h|--help) sed -n '2,30p' "$0"; exit 0 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

case "$PLATFORM" in ios|android|both) ;; *) echo "--platform ios|android|both is required" >&2; exit 2 ;; esac

export JAVA_HOME="${JAVA_HOME:-/opt/homebrew/opt/openjdk@17/libexec/openjdk.jdk/Contents/Home}"
export ANDROID_HOME="${ANDROID_HOME:-$HOME/Library/Android/sdk}"
export PATH="$JAVA_HOME/bin:$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator:$HOME/.maestro/bin:$PATH"
# Maestro talks to the iOS simulator through its own driver; keep its telemetry and update checks quiet.
export MAESTRO_CLI_NO_ANALYTICS=1 MAESTRO_CLI_ANALYSIS_NOTIFICATION_DISABLED=true

MCP_PORT=$((PORT + 1))
DATA_DIR="$(mktemp -d "${TMPDIR:-/tmp}/armada-mobile-e2e.XXXXXX")"
OUTPUT="${OUTPUT:-${DATA_DIR}/maestro}"
mkdir -p "$OUTPUT"
SERVER_PID=""
BOOTED_SIM=""
EMULATOR_PID=""
STATUS=0

log() { echo "[mobile-e2e] $*"; }

cleanup() {
  if [ "$KEEP" = "1" ]; then
    log "--keep: Admiral pid ${SERVER_PID:-none} (data ${DATA_DIR}); simulator ${BOOTED_SIM:-none}; emulator pid ${EMULATOR_PID:-none}"
    return
  fi
  if [ -n "$SERVER_PID" ] && kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
  if [ -n "$BOOTED_SIM" ]; then xcrun simctl shutdown "$BOOTED_SIM" >/dev/null 2>&1 || true; fi
  if [ -n "$EMULATOR_PID" ]; then
    adb -s "$ANDROID_SERIAL" emu kill >/dev/null 2>&1 || kill "$EMULATOR_PID" 2>/dev/null || true
  fi
  rm -rf "${DATA_DIR}/db" "${DATA_DIR}/docks" "${DATA_DIR}/repos"
}
trap cleanup EXIT INT TERM

port_free() { ! (echo >"/dev/tcp/127.0.0.1/$1") 2>/dev/null; }

start_admiral() {
  for p in "$PORT" "$MCP_PORT"; do
    if ! port_free "$p"; then echo "port $p is in use; pass --port" >&2; exit 1; fi
  done
  if [ "$BUILD_SERVER" = "1" ]; then
    log "building Armada.Server (${FRAMEWORK})"
    dotnet build "${REPO_ROOT}/src/Armada.Server/Armada.Server.csproj" -c Release -f "$FRAMEWORK" --nologo -v q >/dev/null
  fi
  mkdir -p "${DATA_DIR}/db" "${DATA_DIR}/logs"
  cat > "${DATA_DIR}/settings.json" <<JSON
{
  "dataDirectory": "${DATA_DIR}",
  "logDirectory": "${DATA_DIR}/logs",
  "docksDirectory": "${DATA_DIR}/docks",
  "reposDirectory": "${DATA_DIR}/repos",
  "admiralPort": ${PORT},
  "mcpPort": ${MCP_PORT},
  "webSocketEnabled": true,
  "dataRetentionDays": 0,
  "syslogServers": [],
  "rest": { "hostname": "127.0.0.1" },
  "database": { "type": "Sqlite", "filename": "${DATA_DIR}/db/armada.db" },
  "repositoryHealth": { "intervalMinutes": 0 },
  "telemetry": { "enabled": false, "prometheusEnabled": false }
}
JSON
  log "starting Admiral on 127.0.0.1:${PORT} (data ${DATA_DIR})"
  ARMADA_DATA_DIR="$DATA_DIR" dotnet "${REPO_ROOT}/src/Armada.Server/bin/Release/${FRAMEWORK}/Armada.Server.dll" \
    > "${DATA_DIR}/server-console.log" 2>&1 &
  SERVER_PID=$!
  for _ in $(seq 1 240); do
    if curl -fsS "http://127.0.0.1:${PORT}/api/v1/status/health" >/dev/null 2>&1; then log "Admiral healthy"; return; fi
    if ! kill -0 "$SERVER_PID" 2>/dev/null; then tail -30 "${DATA_DIR}/server-console.log" >&2; echo "Admiral exited" >&2; exit 1; fi
    sleep 0.5
  done
  echo "Admiral did not become healthy" >&2
  exit 1
}

run_flows() {
  local platform="$1" device="$2" server_url="$3"
  log "running Maestro flows on ${platform} (${device}) against ${server_url}"
  if ! maestro --device "$device" test "$FLOWS" \
      -e SERVER_URL="$server_url" -e APP_ID="$APP_ID" -e PLATFORM="$platform" \
      --format junit --output "${OUTPUT}/${platform}-report.xml" \
      --test-output-dir "${OUTPUT}/${platform}"; then
    STATUS=1
  fi
}

run_ios() {
  local udid
  udid="$(xcrun simctl list devices available -j | python3 -c "
import json, sys
name = sys.argv[1]
for runtime, devices in json.load(sys.stdin)['devices'].items():
    if 'iOS' not in runtime:
        continue
    for d in devices:
        if d['name'] == name and d.get('isAvailable', True):
            print(d['udid']); sys.exit(0)
" "$IOS_DEVICE")"
  [ -n "$udid" ] || { echo "no available iOS simulator named '${IOS_DEVICE}'" >&2; exit 1; }
  if ! xcrun simctl list devices | grep "$udid" | grep -q Booted; then
    log "booting simulator ${IOS_DEVICE}"
    xcrun simctl boot "$udid"
    BOOTED_SIM="$udid"
  fi
  xcrun simctl bootstatus "$udid" -b >/dev/null
  if [ "$BUILD_APP" = "1" ]; then
    log "building the iOS app (Release, embedded bundle)"
    (cd "$MOBILE" && npx expo prebuild --platform ios --no-install >/dev/null && (cd ios && pod install >/dev/null) \
      && npx expo run:ios --configuration Release --device "$udid" --no-bundler)
  fi
  run_flows ios "$udid" "http://127.0.0.1:${PORT}"
}

run_android() {
  if ! adb devices | grep -q "emulator-.*device$"; then
    log "booting emulator ${AVD}"
    emulator -avd "$AVD" -no-snapshot-save -no-boot-anim >/dev/null 2>&1 &
    EMULATOR_PID=$!
  fi
  adb wait-for-device
  ANDROID_SERIAL="$(adb devices | awk '/emulator-.*device$/ {print $1; exit}')"
  export ANDROID_SERIAL
  until [ "$(adb -s "$ANDROID_SERIAL" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ]; do sleep 1; done
  if [ "$BUILD_APP" = "1" ]; then
    log "building the Android app (release variant, embedded bundle)"
    (cd "$MOBILE" && npx expo prebuild --platform android --no-install >/dev/null \
      && npx expo run:android --variant release --device "$AVD" --no-bundler)
  fi
  run_flows android "$ANDROID_SERIAL" "http://10.0.2.2:${PORT}"
}

start_admiral
case "$PLATFORM" in
  ios) run_ios ;;
  android) run_android ;;
  both) run_ios; run_android ;;
esac

log "Maestro output: ${OUTPUT}"
if [ "$STATUS" -ne 0 ]; then log "FAILED"; exit 1; fi
log "PASSED"
