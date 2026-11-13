#!/usr/bin/env bash
# Start the published Armada server (~/.armada/bin/Armada.Server) in the background, detached from this terminal,
# with ~/.armada as its working directory. Uses the A/B slot named by ~/.armada/bin/current when it exists (see
# docs/SERVER_REBUILD.md). Does nothing when an Armada server is already running. Publish the server first with
# publish-server.sh. The Windows Startup launcher picks the slot the same way each time it starts.
# WSL 2 version: WSL shuts a distro down soon after its last terminal closes, which stops a server started from a
# shell. When install-startup.sh has registered the Windows Startup launcher, this starts the server through that
# launcher instead, which keeps the distro running; otherwise it starts it with nohup (output in
# ~/.armada/logs/start-armada-server.log) and says so.
#
# Usage: scripts/wsl2/start-armada-server.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "ERROR: $*" >&2; exit 1; }
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }

# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl2
# shellcheck source=_wsl-common.sh
. "$SCRIPT_DIR/_wsl-common.sh"
# The helper's own errors get the same "ERROR:" prefix as every other Armada script.
wsl_die() { die "$@"; }
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
case "${1:-}" in -h|--help) usage; exit 0 ;; esac
if [ "$#" -gt 0 ]; then
    echo "ERROR: Unexpected argument: $1 (this script takes no arguments; run with -h for usage)" >&2
    exit 2
fi

# Prints the PIDs of running Admirals in this distro, one per line: processes whose executable is Armada.Server (the
# published apphost) or a dotnet host whose arguments name Armada.Server.dll. Nothing is matched by a loose
# command-line pattern. Admirals running on Windows are not seen; stop those with scripts\windows\stop-armada-server.ps1.
armada_server_pids() {
    local pid comm args base
    ps -eo pid=,comm= | while read -r pid comm; do
        [ -n "$pid" ] || continue
        [ "$pid" = "$$" ] && continue
        base="${comm##*/}"
        if [ "$base" = "Armada.Server" ]; then
            echo "$pid"
        elif [ "$base" = "dotnet" ]; then
            args="$(ps -o args= -p "$pid" 2>/dev/null || true)"
            case " $args " in
                *" Armada.Server.dll "*|*"/Armada.Server.dll "*) echo "$pid" ;;
            esac
        fi
    done
}

ARMADA_HOME="$HOME/.armada"
BIN_DIR="$ARMADA_HOME/bin"
SERVER_EXE="$BIN_DIR/Armada.Server"
if [ -f "$BIN_DIR/current" ]; then
    SLOT="$(tr -d '[:space:]' < "$BIN_DIR/current")"
    if [ -n "$SLOT" ] && [ -f "$BIN_DIR/slots/$SLOT/Armada.Server" ]; then
        SERVER_EXE="$BIN_DIR/slots/$SLOT/Armada.Server"
    fi
fi
# -f rather than -x: a home directory on DrvFs may not keep executable bits.
[ -f "$SERVER_EXE" ] || die "Published server executable not found at $SERVER_EXE. Publish it with scripts/wsl2/publish-server.sh first."

# Any Admiral (the slot or the classic publish, or a dotnet-hosted one) already owns the ports.
EXISTING="$(armada_server_pids | tr '\n' ' ')"
if [ -n "${EXISTING// /}" ]; then
    echo "[start-armada-server] Armada.Server is already running (PID ${EXISTING% })."
    exit 0
fi

if wsl_startup_exists armada; then
    echo "[start-armada-server] Starting Armada.Server through the Windows Startup launcher (it keeps the distro running)..."
    wsl_startup_run armada
    exit 0
fi

mkdir -p "$ARMADA_HOME/logs"
cd "$ARMADA_HOME"
nohup "$SERVER_EXE" >>"$ARMADA_HOME/logs/start-armada-server.log" 2>&1 </dev/null &
echo "[start-armada-server] Started $SERVER_EXE (PID $!). Output: $ARMADA_HOME/logs/start-armada-server.log"
echo "[start-armada-server] Note: WSL stops this distro soon after its last terminal closes, and the server with it." >&2
echo "[start-armada-server]       To keep it running, use scripts/wsl2/install-startup.sh." >&2
