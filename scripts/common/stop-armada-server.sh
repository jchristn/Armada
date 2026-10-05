#!/usr/bin/env bash
# =====================================================================
# stop-armada-server.sh -- stop every running Armada Admiral server by PID
# (Linux and macOS counterpart of scripts/windows/stop-armada-server.ps1).
#
# A process counts as an Admiral only when its executable is Armada.Server
# (the published apphost, from ~/.armada/bin or a repo build) or it is a
# dotnet host whose arguments name Armada.Server.dll. Nothing is matched
# by a loose command-line pattern, so an editor, a log tail, or a shell
# whose arguments merely mention Armada.Server is never touched. Each
# Admiral gets SIGTERM, then SIGKILL by PID if it has not exited within
# about 10 seconds.
#
# Usage:
#   stop-armada-server.sh            stop every Admiral, wait for exit
#   stop-armada-server.sh --list     print matching PIDs, stop nothing
#   stop-armada-server.sh -h|--help
#
# Exit codes: 0 nothing left running; 1 an Admiral is still running;
# 2 invalid arguments.
# =====================================================================
set -uo pipefail

LIST_ONLY=0
for arg in "$@"; do
    case "$arg" in
        --list) LIST_ONLY=1 ;;
        -h|--help) grep '^#' "$0" | grep -v '^#!' | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "[stop-armada-server] Unknown argument: $arg" >&2; exit 2 ;;
    esac
done

# Print the PIDs of running Admirals, one per line. "comm" is the executable name on Linux and the executable path
# on macOS; either way its last path component is the program that is running, whatever its arguments say.
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
            if printf '%s\n' "$args" | grep -Eq '(^|[ /])Armada\.Server\.dll( |$)'; then
                echo "$pid"
            fi
        fi
    done
}

PIDS="$(armada_server_pids)"
if [ "$LIST_ONLY" -eq 1 ]; then
    [ -n "$PIDS" ] && printf '%s\n' "$PIDS"
    exit 0
fi

if [ -z "$PIDS" ]; then
    echo "[stop-armada-server] No Armada.Server process is running."
    exit 0
fi

for pid in $PIDS; do
    echo "[stop-armada-server] Stopping PID $pid ($(ps -o args= -p "$pid" 2>/dev/null || echo Armada.Server))"
    kill "$pid" 2>/dev/null || true
done

# Wait for each one to exit; escalate to SIGKILL (by PID) after about 10 seconds.
for attempt in $(seq 1 30); do
    REMAINING=""
    for pid in $PIDS; do
        kill -0 "$pid" 2>/dev/null && REMAINING="$REMAINING $pid"
    done
    [ -z "$REMAINING" ] && exit 0
    if [ "$attempt" -ge 20 ]; then
        for pid in $REMAINING; do kill -9 "$pid" 2>/dev/null || true; done
    fi
    sleep 0.5
done

echo "[stop-armada-server] ERROR: Armada.Server did not stop; still running PID(s):$REMAINING" >&2
exit 1
