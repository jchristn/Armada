#!/usr/bin/env bash
# Stop every running Armada Admiral server in this distro by PID. A process counts as an Admiral only when its
# executable is Armada.Server (the published apphost, from ~/.armada/bin or a repo build) or it is a dotnet host whose
# arguments name Armada.Server.dll; nothing is matched by a loose command-line pattern. Each Admiral gets SIGTERM,
# then SIGKILL by PID if it has not exited within about 10 seconds. A systemd user unit left by the scripts/linux
# version would restart it, so that unit is stopped first.
# WSL 2 version: Admirals running on Windows are not touched; stop those with scripts\windows\stop-armada-server.ps1.
#
# Usage: scripts/wsl2/stop-armada-server.sh [--list]
#   --list   print the matching PIDs and stop nothing
# Exit codes: 0 nothing left running; 1 an Admiral is still running; 2 invalid arguments.
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
LIST_ONLY=0
for arg in "$@"; do
    case "$arg" in
        --list) LIST_ONLY=1 ;;
        *) echo "[stop-armada-server] Unknown argument: $arg" >&2; exit 2 ;;
    esac
done

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

# Stops every Admiral in this distro: SIGTERM, then SIGKILL by PID after about 10 seconds. Returns 1 when one is
# still running. A systemd user unit left by the Linux scripts would restart it, so that is stopped first.
stop_servers() {
    local pids pid attempt remaining
    if [ -f "${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user/armada.service" ] && wsl_has_systemd; then
        systemctl --user stop armada.service >/dev/null 2>&1 || true
    fi
    pids="$(armada_server_pids)"
    if [ -z "$pids" ]; then
        echo "[stop-armada-server] No Armada.Server process is running."
        return 0
    fi
    for pid in $pids; do
        echo "[stop-armada-server] Stopping PID $pid ($(ps -o args= -p "$pid" 2>/dev/null || echo Armada.Server))"
        kill "$pid" 2>/dev/null || true
    done
    for attempt in $(seq 1 30); do
        remaining=""
        for pid in $pids; do
            kill -0 "$pid" 2>/dev/null && remaining="$remaining $pid"
        done
        [ -z "$remaining" ] && return 0
        if [ "$attempt" -ge 20 ]; then
            for pid in $remaining; do kill -9 "$pid" 2>/dev/null || true; done
        fi
        sleep 0.5
    done
    echo "[stop-armada-server] ERROR: Armada.Server did not stop; still running PID(s):$remaining" >&2
    return 1
}

if [ "$LIST_ONLY" -eq 1 ]; then
    armada_server_pids
    exit 0
fi
stop_servers
