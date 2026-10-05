#!/usr/bin/env bash
# =====================================================================
# verify-macos-pkg.sh -- install verification for the macOS server .pkg
# (pkg-server channel) (V1 readiness W5.5).
#
# 1. Builds the .pkg with Armada.Publisher (or uses --pkg <file>).
# 2. Expands it with pkgutil and checks the layout the installer would
#    lay down: /usr/local/lib/armada-server/armada-server (Mach-O for this
#    Mac's architecture), the /usr/local/bin/armada-server symlink, the
#    React dashboard, uninstall.sh, and a postinstall that registers the
#    service with --install-service.
# 3. Installs the payload into a temp root (no sudo, nothing system-wide),
#    runs "armada-server --install-service --dry-run" (prints the launchd
#    agent, changes nothing), then starts the installed binary with
#    ARMADA_DATA_DIR and HOME in the temp directory and runs the REST
#    smoke test (login, dashboard, fleet, vessel, one mission).
#
# Usage: verify-macos-pkg.sh [--pkg <file>] [--version X.Y.Z] [--keep]
# Env:   IV_PORT_BASE (default 34000), IV_ADMIN_PASSWORD
# =====================================================================
set -uo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

PKG=""
VERSION=""
KEEP=0
while [ $# -gt 0 ]; do
  case "$1" in
    --pkg) PKG="$2"; shift 2 ;;
    --version) VERSION="$2"; shift 2 ;;
    --keep) KEEP=1; shift ;;
    -h|--help) grep '^#' "$0" | grep -v '^#!' | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

[ "$(uname -s)" = "Darwin" ] || iv_die "this test runs on macOS only"
PORT_BASE="${IV_PORT_BASE:-34000}"
REST_PORT=$((PORT_BASE + 30))
MCP_PORT=$((PORT_BASE + 31))
STUB_PORT=$((PORT_BASE + 51))
PASSWORD="${IV_ADMIN_PASSWORD:-Install-Verify-$RANDOM-Pw1}"
case "$(uname -m)" in arm64) RID="osx-arm64"; ARCH="arm64" ;; *) RID="osx-x64"; ARCH="x86_64" ;; esac
if [ -z "$VERSION" ]; then
  VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$IV_REPO_ROOT/src/Directory.Build.props" | head -1)"
fi

for p in "$REST_PORT" "$MCP_PORT" "$STUB_PORT"; do iv_require_free_port "$p"; done

IV_WORK="$(mktemp -d "${TMPDIR:-/tmp}/armada-iv-pkg.XXXXXX")"
cleanup() {
  iv_stop_pids
  if [ "$KEEP" -eq 0 ]; then rm -rf "$IV_WORK"; else iv_log "work directory kept: $IV_WORK"; fi
}
trap cleanup EXIT

FAIL=0
check() {
  if eval "$2"; then echo "PASS  $1"; else echo "FAIL  $1"; FAIL=1; fi
}

if [ -z "$PKG" ]; then
  iv_log "building pkg-server $VERSION with Armada.Publisher (output $IV_WORK/out)"
  ( cd "$IV_REPO_ROOT" && dotnet run --project src/Armada.Publisher -c Release -- \
      --channel pkg-server --version "$VERSION" --output "$IV_WORK/out" ) > "$IV_WORK/publish.log" 2>&1 \
    || { tail -40 "$IV_WORK/publish.log"; iv_die "pkg build failed"; }
  grep -E "^\[(pkg|sign|publish)\]" "$IV_WORK/publish.log" | cut -c1-200 || true
  PKG="$(find "$IV_WORK/out" -name "armada-server-*-${RID}.pkg" | head -1)"
fi
[ -f "$PKG" ] || iv_die "no ${RID} .pkg found"
iv_log "package: $PKG ($(du -h "$PKG" | cut -f1))"
pkgutil --check-signature "$PKG" 2>&1 | head -3 || true

pkgutil --expand-full "$PKG" "$IV_WORK/expanded" || iv_die "pkgutil --expand-full failed"
COMPONENT="$(find "$IV_WORK/expanded" -maxdepth 1 -type d -name '*.pkg' | head -1)"
[ -n "$COMPONENT" ] || iv_die "no component package inside $PKG"
PAYLOAD="$COMPONENT/Payload"
SCRIPTS="$COMPONENT/Scripts"
LIB="$PAYLOAD/usr/local/lib/armada-server"

# usr/local/bin/armada-server is an absolute symlink to the executable (named after the assembly).
TARGET="$(readlink "$PAYLOAD/usr/local/bin/armada-server" 2>/dev/null)"
EXE_NAME="$(basename "${TARGET:-missing}")"
check "payload links usr/local/bin/armada-server into /usr/local/lib/armada-server" \
  "case '$TARGET' in /usr/local/lib/armada-server/*) true ;; *) false ;; esac"
check "symlink target $TARGET exists in the payload" "[ -x '$LIB/$EXE_NAME' ]"
check "server binary is Mach-O for ${ARCH}" "file '$LIB/$EXE_NAME' | grep -q '${ARCH}'"
check "payload ships the React dashboard" "[ -f '$LIB/dashboard/index.html' ] && ls '$LIB/dashboard/assets/' | grep -q '\.js$'"
check "payload ships uninstall.sh" "[ -x '$LIB/uninstall.sh' ] && grep -q -- '--uninstall-service' '$LIB/uninstall.sh'"
check "postinstall registers the service with --install-service" "grep -q -- '--install-service' '$SCRIPTS/postinstall'"
check "preinstall stops a previous agent" "grep -q 'launchctl bootout' '$SCRIPTS/preinstall'"

# Install into a temp root: the same files at the same relative paths, no system changes.
ROOT="$IV_WORK/root"
mkdir -p "$ROOT"
( cd "$PAYLOAD" && tar -cf - . ) | ( cd "$ROOT" && tar -xf - )
SERVER="$ROOT$TARGET"
check "installed binary is executable in the temp root" "[ -x '$SERVER' ]"

mkdir -p "$IV_WORK/home"
export HOME="$IV_WORK/home"
export ARMADA_DATA_DIR="$IV_WORK/data"
export ARMADA_INITIAL_ADMIN_PASSWORD="$PASSWORD"
cd "$IV_WORK" || exit 1

iv_log "armada-server --install-service --dry-run"
"$SERVER" --install-service --dry-run > "$IV_WORK/dry-run.log" 2>&1
DRY_CODE=$?
sed 's/^/      /' "$IV_WORK/dry-run.log" | head -60
check "--install-service --dry-run exits 0" "[ $DRY_CODE -eq 0 ]"
check "dry run describes the launchd agent" "grep -q 'com.joelchristner.armada.server' '$IV_WORK/dry-run.log'"
check "dry run wrote no LaunchAgents plist" "[ ! -e '$HOME/Library/LaunchAgents/com.joelchristner.armada.server.plist' ]"

iv_write_settings "$ARMADA_DATA_DIR" "$REST_PORT" "$MCP_PORT"
iv_make_origin "$IV_WORK/origin.git"
iv_start_stub "$STUB_PORT" "$IV_WORK/stub.log"

iv_log "starting $SERVER"
"$SERVER" > "$IV_WORK/server.out" 2>&1 &
SERVER_PID=$!
IV_PIDS+=("$SERVER_PID")

iv_smoke --base-url "http://127.0.0.1:${REST_PORT}" --password "$PASSWORD" \
  --stub-url "http://127.0.0.1:${STUB_PORT}/v1" --repo-url "$IV_WORK/origin.git" || FAIL=1

if [ "$FAIL" -ne 0 ]; then
  iv_log "server output tail:"; tail -30 "$IV_WORK/server.out"
fi

if [ "$FAIL" -eq 0 ]; then iv_log "macOS .pkg install verification: PASS"; else iv_log "macOS .pkg install verification: FAIL"; fi
exit "$FAIL"
