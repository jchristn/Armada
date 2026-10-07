#!/usr/bin/env bash
# Mobile end-to-end tests (Maestro) against a throwaway Admiral.
#
# What it does, reproducibly:
#   1. builds Armada.Server (Release) from this checkout unless --no-server-build;
#   2. starts a throwaway Admiral on 127.0.0.1 with ARMADA_DATA_DIR in a temp directory (never ~/.armada) and
#      ports from --port (default 44010; MCP is port+1; Prometheus is off). It keeps the default admin password,
#      which the Admiral allows on loopback, so the flows exercise the "Skip for now" password-change path. It
#      seeds, through the REST API, a fleet with two vessels (local git repos in the temp directory, one with commits
#      on several days), an environment that requires approval, and one deployment waiting for it (the approvals flow
#      approves it);
#   3. builds and installs a Release build of the app (JS bundle embedded, no Metro) unless --no-app-build;
#   4. runs every flow in src/Armada.Mobile/e2e with SERVER_URL pointing at the Admiral as the device sees it
#      (127.0.0.1 from the iOS simulator, 10.0.2.2 from the Android emulator);
#   5. stops the Admiral, and shuts down any simulator or emulator it booted.
#
# With --proxy it also starts a throwaway Armada.Proxy (port --proxy-port, default port+10, data in the same temp
# directory), enables the Admiral's remote-control tunnel to it, and after the flows above runs the flows in
# src/Armada.Mobile/e2e/proxy with PROXY_URL, PROXY_PASSWORD, and PROXY_INSTANCE: sign in to the proxy, pick the
# Admiral, sign in to it through the relay, and load screens over the relayed REST API and WebSocket.
#
# Usage:
#   scripts/mobile/run-e2e.sh --platform ios [--device "iPhone 17"] [--port 44010] [--no-app-build] [--no-server-build]
#   scripts/mobile/run-e2e.sh --platform android [--avd Armada_Phone] [--port 44010] [--no-app-build]
#   scripts/mobile/run-e2e.sh --platform both
#   scripts/mobile/run-e2e.sh --platform ios --proxy [--proxy-port 44020] [--proxy-only]
# Options:
#   --keep            leave the Admiral, simulator, and emulator running (prints how to stop them)
#   --output DIR      Maestro reports and screenshots (default: a temp directory, printed at the end)
#   --flows PATH      a single flow file or folder (default: src/Armada.Mobile/e2e)
#   --proxy           also start Armada.Proxy and run the proxy flows
#   --proxy-only      with --proxy, run only the proxy flows
#   --push-sim        iOS only: also run src/Armada.Mobile/e2e/push-sim, delivering simulated pushes with
#                     `xcrun simctl push` (an APNs payload shaped like Expo's) while the tap flows run
#   --push-sim-only   run only the push-sim flows
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
PROXY=0
PROXY_ONLY=0
PROXY_PORT=""
PROXY_PASSWORD="e2e-proxy-$(od -An -N6 -tx1 /dev/urandom | tr -d ' \n')"
PROXY_INSTANCE="armada-e2e"
PROXY_PID=""
PUSH_SIM=0
PUSH_SIM_ONLY=0
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
    --proxy) PROXY=1; shift ;;
    --proxy-only) PROXY=1; PROXY_ONLY=1; shift ;;
    --proxy-port) PROXY_PORT="$2"; shift 2 ;;
    --push-sim) PUSH_SIM=1; shift ;;
    --push-sim-only) PUSH_SIM=1; PUSH_SIM_ONLY=1; shift ;;
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
PROXY_PORT="${PROXY_PORT:-$((PORT + 10))}"
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
    log "--keep: Admiral pid ${SERVER_PID:-none} (data ${DATA_DIR}); proxy pid ${PROXY_PID:-none}; simulator ${BOOTED_SIM:-none}; emulator pid ${EMULATOR_PID:-none}"
    return
  fi
  if [ -n "$SERVER_PID" ] && kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
  if [ -n "$PROXY_PID" ] && kill -0 "$PROXY_PID" 2>/dev/null; then
    kill "$PROXY_PID" 2>/dev/null || true
    wait "$PROXY_PID" 2>/dev/null || true
  fi
  if [ -n "$BOOTED_SIM" ]; then xcrun simctl shutdown "$BOOTED_SIM" >/dev/null 2>&1 || true; fi
  if [ -n "$EMULATOR_PID" ]; then
    adb -s "$ANDROID_SERIAL" emu kill >/dev/null 2>&1 || kill "$EMULATOR_PID" 2>/dev/null || true
  fi
  rm -rf "${DATA_DIR}/db" "${DATA_DIR}/docks" "${DATA_DIR}/repos"
}
trap cleanup EXIT INT TERM

port_free() { ! (echo >"/dev/tcp/127.0.0.1/$1") 2>/dev/null; }

start_proxy() {
  if ! port_free "$PROXY_PORT"; then echo "port $PROXY_PORT is in use; pass --proxy-port" >&2; exit 1; fi
  if [ "$BUILD_SERVER" = "1" ]; then
    log "building Armada.Proxy (${FRAMEWORK})"
    dotnet build "${REPO_ROOT}/src/Armada.Proxy/Armada.Proxy.csproj" -c Release -f "$FRAMEWORK" --nologo -v q >/dev/null
  fi
  mkdir -p "${DATA_DIR}/proxy/logs"
  cat > "${DATA_DIR}/proxy/proxysettings.json" <<JSON
{ "ArmadaProxy": { "dataDirectory": "${DATA_DIR}/proxy", "logDirectory": "${DATA_DIR}/proxy/logs", "hostname": "127.0.0.1", "port": ${PROXY_PORT} } }
JSON
  log "starting Armada.Proxy on 127.0.0.1:${PROXY_PORT}"
  (cd "${DATA_DIR}/proxy" && ARMADA_PROXY_PASSWORD="$PROXY_PASSWORD" exec dotnet "${REPO_ROOT}/src/Armada.Proxy/bin/Release/${FRAMEWORK}/Armada.Proxy.dll" \
    --config "${DATA_DIR}/proxy/proxysettings.json" > "${DATA_DIR}/proxy-console.log" 2>&1) &
  PROXY_PID=$!
  for _ in $(seq 1 240); do
    if curl -fsS "http://127.0.0.1:${PROXY_PORT}/proxy-api/v1/status/health" >/dev/null 2>&1; then log "Armada.Proxy healthy"; return; fi
    if ! kill -0 "$PROXY_PID" 2>/dev/null; then tail -30 "${DATA_DIR}/proxy-console.log" >&2; echo "Armada.Proxy exited" >&2; exit 1; fi
    sleep 0.5
  done
  echo "Armada.Proxy did not become healthy" >&2
  exit 1
}

wait_for_tunnel() {
  for _ in $(seq 1 120); do
    if curl -fsS "http://127.0.0.1:${PROXY_PORT}/proxy-api/v1/status/health" 2>/dev/null | grep -q '"connectedInstances":[1-9]'; then
      log "Admiral tunnel connected to the proxy"
      return
    fi
    sleep 0.5
  done
  echo "the Admiral did not connect its tunnel to the proxy" >&2
  exit 1
}

start_admiral() {
  REMOTE_CONTROL_JSON=""
  if [ "$PROXY" = "1" ]; then
    REMOTE_CONTROL_JSON=",
  \"remoteControl\": { \"enabled\": true, \"tunnelUrl\": \"ws://127.0.0.1:${PROXY_PORT}/tunnel\", \"instanceId\": \"${PROXY_INSTANCE}\", \"password\": \"${PROXY_PASSWORD}\", \"heartbeatIntervalSeconds\": 10 }"
  fi
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
  "telemetry": { "enabled": false, "prometheusEnabled": false }${REMOTE_CONTROL_JSON}
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

# Seed what the flows need through the REST API, as the default admin: one deployment awaiting approval, and for the
# Build flows a fleet holding the vessel plus a second vessel whose repository has commits on several days (View
# History). No captain is seeded: the Ask flow expects none, and the Build flows create one through the app.
seed_admiral() {
  local base="http://127.0.0.1:${PORT}" token repo repo2 fleet vessel env day stamp
  token="$(curl -fsS -X POST "${base}/api/v1/authenticate" -H 'Content-Type: application/json' \
    -d '{"email":"admin@armada","password":"password","tenantId":"default"}' | python3 -c 'import json,sys; print(json.load(sys.stdin)["Token"])')"
  repo="${DATA_DIR}/seed-repo"
  mkdir -p "$repo"
  git -C "$repo" init -q -b main
  echo "seed" > "${repo}/README.md"
  git -C "$repo" add README.md
  git -C "$repo" -c user.email=e2e@armada -c user.name=e2e commit -qm seed
  repo2="${DATA_DIR}/seed-repo-web"
  mkdir -p "$repo2"
  git -C "$repo2" init -q -b main
  for day in 1 2 2 5 9; do
    echo "change ${day} $RANDOM" >> "${repo2}/CHANGES.md"
    git -C "$repo2" add CHANGES.md
    stamp="$(python3 -c 'import datetime,sys; print((datetime.datetime.now(datetime.timezone.utc) - datetime.timedelta(days=int(sys.argv[1]))).strftime("%Y-%m-%dT12:00:00Z"))' "$day")"
    GIT_AUTHOR_DATE="$stamp" GIT_COMMITTER_DATE="$stamp" git -C "$repo2" -c user.email=e2e@armada -c user.name=e2e commit -qm "Change from ${day} days ago"
  done
  id_of() { python3 -c 'import json,sys; print(json.load(sys.stdin)["Id"])'; }
  fleet="$(curl -fsS -X POST "${base}/api/v1/fleets" -H "X-Token: ${token}" -H 'Content-Type: application/json' \
    -d '{"name":"demo-fleet","description":"Fleet seeded for the mobile E2E flows"}' | id_of)"
  vessel="$(curl -fsS -X POST "${base}/api/v1/vessels" -H "X-Token: ${token}" -H 'Content-Type: application/json' \
    -d "{\"name\":\"demo-api\",\"repoUrl\":\"${repo}\",\"defaultBranch\":\"main\",\"fleetId\":\"${fleet}\"}" | id_of)"
  curl -fsS -X POST "${base}/api/v1/vessels" -H "X-Token: ${token}" -H 'Content-Type: application/json' \
    -d "{\"name\":\"demo-web\",\"repoUrl\":\"${repo2}\",\"localPath\":\"${repo2}\",\"defaultBranch\":\"main\",\"fleetId\":\"${fleet}\"}" >/dev/null
  env="$(curl -fsS -X POST "${base}/api/v1/environments" -H "X-Token: ${token}" -H 'Content-Type: application/json' \
    -d "{\"vesselId\":\"${vessel}\",\"name\":\"production\",\"kind\":\"Production\",\"requiresApproval\":true}" | id_of)"
  curl -fsS -X POST "${base}/api/v1/deployments" -H "X-Token: ${token}" -H 'Content-Type: application/json' \
    -d "{\"vesselId\":\"${vessel}\",\"environmentId\":\"${env}\",\"title\":\"Release 2.3\",\"autoExecute\":false}" >/dev/null
  log "seeded fleet ${fleet}, vessels demo-api (${vessel}) and demo-web, and a deployment awaiting approval (environment ${env})"
}

run_flows() {
  local platform="$1" device="$2" server_url="$3" proxy_url="$4"
  if [ "$PROXY_ONLY" != "1" ] && [ "$PUSH_SIM_ONLY" != "1" ]; then
    log "running Maestro flows on ${platform} (${device}) against ${server_url}"
    if ! maestro --device "$device" test "$FLOWS" \
        -e SERVER_URL="$server_url" -e APP_ID="$APP_ID" -e PLATFORM="$platform" \
        --format junit --output "${OUTPUT}/${platform}-report.xml" \
        --test-output-dir "${OUTPUT}/${platform}"; then
      STATUS=1
    fi
  fi
  if [ "$PROXY" = "1" ] && [ "$PUSH_SIM_ONLY" != "1" ]; then
    log "running Maestro proxy flows on ${platform} (${device}) against ${proxy_url}"
    if ! maestro --device "$device" test "${MOBILE}/e2e/proxy" \
        -e PROXY_URL="$proxy_url" -e PROXY_PASSWORD="$PROXY_PASSWORD" -e PROXY_INSTANCE="$PROXY_INSTANCE" \
        -e APP_ID="$APP_ID" -e PLATFORM="$platform" \
        --format junit --output "${OUTPUT}/${platform}-proxy-report.xml" \
        --test-output-dir "${OUTPUT}/${platform}-proxy"; then
      STATUS=1
    fi
  fi
}

# Run one push-sim flow while delivering the given APNs payload every few seconds until the flow ends (a banner
# lasts a few seconds; repeating it removes any race with Maestro's startup).
push_sim_flow() {
  local udid="$1" flow="$2" payload="$3" name
  name="$(basename "$flow" .yaml)"
  printf '%s' "$payload" > "${DATA_DIR}/${name}.apns"
  maestro --device "$udid" test "$flow" -e APP_ID="$APP_ID" -e SERVER_URL="http://127.0.0.1:${PORT}" \
    --format junit --output "${OUTPUT}/ios-${name}-report.xml" --test-output-dir "${OUTPUT}/ios-push-sim" &
  local mpid=$!
  while kill -0 "$mpid" 2>/dev/null; do
    xcrun simctl push "$udid" "$APP_ID" "${DATA_DIR}/${name}.apns" >/dev/null 2>&1 || true
    sleep 3
  done
  wait "$mpid" || STATUS=1
}

run_push_sim() {
  local udid="$1" dir="${MOBILE}/e2e/push-sim"
  log "running simulated push flows on iOS (${udid})"
  if ! maestro --device "$udid" test "${dir}/01-enable-notifications.yaml" -e APP_ID="$APP_ID" -e SERVER_URL="http://127.0.0.1:${PORT}" \
      --format junit --output "${OUTPUT}/ios-push-enable-report.xml" --test-output-dir "${OUTPUT}/ios-push-sim"; then
    STATUS=1
    return
  fi
  push_sim_flow "$udid" "${dir}/02-tap-notification.yaml" \
    '{"aps":{"alert":{"title":"E2E permission request","body":"Captain wants to use Bash"},"category":"armada_approve_deny","badge":1,"sound":"default"},"body":{"url":"/cli-permissions?request=cpr_e2e_push1","kind":"cli_permission","entityId":"cpr_e2e_push1","category":"CliPermission"}}'
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
  run_flows ios "$udid" "http://127.0.0.1:${PORT}" "http://127.0.0.1:${PROXY_PORT}"
  if [ "$PUSH_SIM" = "1" ]; then run_push_sim "$udid"; fi
}

# The serial of the running emulator whose AVD is $AVD (other emulators may be running), or nothing.
avd_serial() {
  local serial
  for serial in $(adb devices | awk '/emulator-.*device$/ {print $1}'); do
    if [ "$(adb -s "$serial" emu avd name 2>/dev/null | head -1 | tr -d '\r')" = "$AVD" ]; then echo "$serial"; return; fi
  done
}

run_android() {
  ANDROID_SERIAL="$(avd_serial)"
  if [ -z "$ANDROID_SERIAL" ]; then
    log "booting emulator ${AVD}"
    emulator -avd "$AVD" -no-snapshot-save -no-boot-anim >/dev/null 2>&1 &
    EMULATOR_PID=$!
    for _ in $(seq 1 240); do
      ANDROID_SERIAL="$(avd_serial)"
      [ -n "$ANDROID_SERIAL" ] && break
      sleep 1
    done
    [ -n "$ANDROID_SERIAL" ] || { echo "emulator ${AVD} did not come up" >&2; exit 1; }
  fi
  export ANDROID_SERIAL
  until [ "$(adb -s "$ANDROID_SERIAL" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ]; do sleep 1; done
  if [ "$BUILD_APP" = "1" ]; then
    log "building the Android app (release variant, embedded bundle)"
    (cd "$MOBILE" && npx expo prebuild --platform android --no-install >/dev/null \
      && npx expo run:android --variant release --device "$AVD" --no-bundler)
  fi
  run_flows android "$ANDROID_SERIAL" "http://10.0.2.2:${PORT}" "http://10.0.2.2:${PROXY_PORT}"
}

if [ "$PROXY" = "1" ]; then start_proxy; fi
start_admiral
if [ "$PROXY" = "1" ]; then wait_for_tunnel; fi
seed_admiral
case "$PLATFORM" in
  ios) run_ios ;;
  android) run_android ;;
  both) run_ios; run_android ;;
esac

log "Maestro output: ${OUTPUT}"
if [ "$STATUS" -ne 0 ]; then log "FAILED"; exit 1; fi
log "PASSED"
