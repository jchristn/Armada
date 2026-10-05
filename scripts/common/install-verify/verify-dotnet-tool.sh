#!/usr/bin/env bash
# =====================================================================
# verify-dotnet-tool.sh -- install verification for the NuGet global tool
# (Armada.Helm, command "armada") on Linux and macOS (V1 readiness W5.5).
#
# 1. Packs src/Armada.Helm into a local feed (or uses --nupkg-dir).
# 2. Installs it with "dotnet tool install --tool-path <temp>" -- the same
#    package "dotnet tool install -g Armada.Helm" would install, without
#    touching the global tool directory.
# 3. With ARMADA_DATA_DIR and HOME pointed into the temp directory and the
#    Admiral on ports IV_PORT_BASE+10 / +11, runs "armada server start",
#    then the REST smoke test (login, dashboard, fleet, vessel from a temp
#    bare repo, one mission on a stub-inference ApiEndpoint captain), then
#    "armada server stop".
#
# Usage: verify-dotnet-tool.sh [--nupkg-dir <dir>] [--keep]
# Env:   IV_PORT_BASE (default 34000), IV_ADMIN_PASSWORD
# Never touches ~/.armada or the global dotnet tools directory.
# =====================================================================
set -uo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

NUPKG_DIR=""
KEEP=0
while [ $# -gt 0 ]; do
  case "$1" in
    --nupkg-dir) NUPKG_DIR="$2"; shift 2 ;;
    --keep) KEEP=1; shift ;;
    -h|--help) grep '^#' "$0" | grep -v '^#!' | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

PORT_BASE="${IV_PORT_BASE:-34000}"
REST_PORT=$((PORT_BASE + 10))
MCP_PORT=$((PORT_BASE + 11))
STUB_PORT=$((PORT_BASE + 50))
PASSWORD="${IV_ADMIN_PASSWORD:-Install-Verify-$RANDOM-Pw1}"

IV_WORK="$(mktemp -d "${TMPDIR:-/tmp}/armada-iv-tool.XXXXXX")"
SERVER_PID=""
TOOL=""
cleanup() {
  if [ -n "$TOOL" ] && [ -x "$TOOL" ] && [ -n "$SERVER_PID" ] && kill -0 "$SERVER_PID" 2>/dev/null; then
    env HOME="$IV_WORK/home" ARMADA_DATA_DIR="$IV_WORK/data" "$TOOL" server stop >/dev/null 2>&1 || true
  fi
  [ -n "$SERVER_PID" ] && IV_PIDS+=("$SERVER_PID")
  iv_stop_pids
  if [ "$KEEP" -eq 0 ]; then rm -rf "$IV_WORK"; else iv_log "work directory kept: $IV_WORK"; fi
}
trap cleanup EXIT

for p in "$REST_PORT" "$MCP_PORT" "$STUB_PORT"; do iv_require_free_port "$p"; done
command -v dotnet >/dev/null 2>&1 || iv_die "dotnet SDK is required"
command -v git >/dev/null 2>&1 || iv_die "git is required"

if [ -z "$NUPKG_DIR" ]; then
  NUPKG_DIR="$IV_WORK/nupkg"
  iv_log "packing src/Armada.Helm into $NUPKG_DIR"
  dotnet pack "$IV_REPO_ROOT/src/Armada.Helm" -c Release -o "$NUPKG_DIR" --nologo -v q > "$IV_WORK/pack.log" 2>&1 \
    || { tail -40 "$IV_WORK/pack.log"; iv_die "dotnet pack failed"; }
fi
NUPKG="$(find "$NUPKG_DIR" -maxdepth 1 -name 'Armada.Helm.*.nupkg' | head -1)"
[ -n "$NUPKG" ] || iv_die "no Armada.Helm nupkg in $NUPKG_DIR"
VERSION="$(basename "$NUPKG" .nupkg)"; VERSION="${VERSION#Armada.Helm.}"
iv_log "package: $(basename "$NUPKG") ($(du -h "$NUPKG" | cut -f1))"

# Install exactly as a user would, but into a private tool path. --ignore-failed-sources keeps
# the install offline-tolerant; the package has no NuGet dependencies of its own.
iv_log "dotnet tool install Armada.Helm $VERSION --tool-path $IV_WORK/tools"
dotnet tool install Armada.Helm --version "$VERSION" --tool-path "$IV_WORK/tools" \
  --add-source "$NUPKG_DIR" --ignore-failed-sources > "$IV_WORK/install.log" 2>&1 \
  || { cat "$IV_WORK/install.log"; iv_die "dotnet tool install failed"; }
cat "$IV_WORK/install.log"
TOOL="$IV_WORK/tools/armada"
[ -x "$TOOL" ] || iv_die "tool shim not found at $TOOL"

mkdir -p "$IV_WORK/home"
export HOME="$IV_WORK/home"
export ARMADA_DATA_DIR="$IV_WORK/data"
export ARMADA_INITIAL_ADMIN_PASSWORD="$PASSWORD"
# Run from the temp dir so the CLI cannot find a source checkout and build the server from it:
# the test must use what the package installed.
cd "$IV_WORK" || exit 1

iv_write_settings "$ARMADA_DATA_DIR" "$REST_PORT" "$MCP_PORT"
iv_make_origin "$IV_WORK/origin.git"
iv_start_stub "$STUB_PORT" "$IV_WORK/stub.log"

iv_log "armada --version"
"$TOOL" --version 2>&1 | tail -3 || true

iv_log "armada server start"
"$TOOL" server start > "$IV_WORK/server-start.log" 2>&1
START_CODE=$?
cat "$IV_WORK/server-start.log"
SERVER_PID="$(sed -n 's/.*(PID: \([0-9][0-9]*\)).*/\1/p' "$IV_WORK/server-start.log" | head -1)"
[ "$START_CODE" -eq 0 ] && [ -n "$SERVER_PID" ] || iv_die "armada server start failed (exit $START_CODE)"

RESULT=0
iv_smoke --base-url "http://127.0.0.1:${REST_PORT}" --password "$PASSWORD" \
  --stub-url "http://127.0.0.1:${STUB_PORT}/v1" --repo-url "$IV_WORK/origin.git" || RESULT=1

iv_log "armada server status"
"$TOOL" server status 2>&1 | tail -5 || true

if [ "$RESULT" -ne 0 ]; then
  iv_log "admiral log tail:"
  tail -60 "$ARMADA_DATA_DIR/logs/"*.log 2>/dev/null || true
fi

iv_log "armada server stop"
"$TOOL" server stop 2>&1 | tail -3 || true
for i in $(seq 1 30); do kill -0 "$SERVER_PID" 2>/dev/null || break; sleep 1; done
if kill -0 "$SERVER_PID" 2>/dev/null; then iv_log "server PID $SERVER_PID still running after stop; killing it"; RESULT=1; fi

if [ "$RESULT" -eq 0 ]; then iv_log "dotnet tool install verification: PASS"; else iv_log "dotnet tool install verification: FAIL"; fi
exit "$RESULT"
