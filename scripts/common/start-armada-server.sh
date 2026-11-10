#!/usr/bin/env bash
# Start the published Armada server (~/.armada/bin/Armada.Server) in the background, detached from this terminal,
# with ~/.armada as its working directory. Uses the A/B slot named by ~/.armada/bin/current when it exists (see
# docs/SERVER_REBUILD.md). Does nothing when an Armada server is already running. Output goes to
# ~/.armada/logs/start-armada-server.log. Publish the server first with publish-server.sh.
#
# Usage: start-armada-server.sh
set -euo pipefail

usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }
case "${1:-}" in -h|--help) usage; exit 0 ;; esac
if [ "$#" -gt 0 ]; then
    echo "ERROR: Unexpected argument: $1 (this script takes no arguments; run with -h for usage)" >&2
    exit 2
fi

ARMADA_HOME="${HOME}/.armada"
BIN_DIR="${ARMADA_HOME}/bin"
SERVER_EXE="${BIN_DIR}/Armada.Server"
if [ -f "${BIN_DIR}/current" ]; then
    SLOT="$(tr -d '[:space:]' < "${BIN_DIR}/current")"
    if [ -n "$SLOT" ] && [ -x "${BIN_DIR}/slots/${SLOT}/Armada.Server" ]; then
        SERVER_EXE="${BIN_DIR}/slots/${SLOT}/Armada.Server"
    fi
fi

if [ ! -x "$SERVER_EXE" ]; then
    echo "ERROR: Published server executable not found at ${SERVER_EXE}. Publish it with publish-server.sh first." >&2
    exit 1
fi

# Any Admiral (the slot or the classic publish, or a dotnet-hosted one) already owns the ports.
EXISTING="$("$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/stop-armada-server.sh" --list | tr '\n' ' ')"
if [ -n "${EXISTING// /}" ]; then
    echo "[start-armada-server] Armada.Server is already running (PID ${EXISTING% })."
    exit 0
fi

mkdir -p "${ARMADA_HOME}/logs"
cd "$ARMADA_HOME"
nohup "$SERVER_EXE" >>"${ARMADA_HOME}/logs/start-armada-server.log" 2>&1 </dev/null &
echo "[start-armada-server] Started ${SERVER_EXE} (PID $!). Output: ${ARMADA_HOME}/logs/start-armada-server.log"
