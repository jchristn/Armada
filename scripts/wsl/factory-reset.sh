#!/usr/bin/env bash
# Wipe Armada back to a factory-fresh state: stops every Armada.Server in this distro (by PID, after stopping a
# systemd user unit left by the scripts/linux version), then deletes the database and all runtime state under
# ~/.armada so the next start comes up empty as if freshly deployed. By default it KEEPS the deployed server bin and
# dashboard so the deployment still runs; pass --all to remove those too. The start-at-login registration stays.
# NOTE: this regenerates the API key on next start; MCP clients and the CLI that stored the old key must be
# reconfigured.
# WSL 1 version: resets this distro's ~/.armada only; use scripts\windows\factory-reset.bat for the Windows one.
#
# Usage: scripts/wsl/factory-reset.sh [-y|--yes|--force] [--all]
#   -y, --yes, --force   skip the confirmation prompt
#   --all                also delete bin and dashboard (the whole ~/.armada)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "ERROR: $*" >&2; exit 1; }
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }

# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl
# shellcheck source=_wsl-common.sh
. "$SCRIPT_DIR/_wsl-common.sh"
# The helper's own errors get the same "ERROR:" prefix as every other Armada script.
wsl_die() { die "$@"; }
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
case "${1:-}" in -h|--help) usage; exit 0 ;; esac
ARMADA_DIR="$HOME/.armada"
FORCE=0
WIPE_ALL=0
for arg in "$@"; do
    case "$arg" in
        -y|--yes|--force) FORCE=1 ;;
        --all) WIPE_ALL=1 ;;
        *) echo "[factory-reset] Unknown argument: $arg" >&2
           echo "Usage: scripts/wsl/factory-reset.sh [-y|--yes|--force] [--all]" >&2
           exit 2 ;;
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

if [ ! -d "$ARMADA_DIR" ]; then
    echo "[factory-reset] Nothing to reset: $ARMADA_DIR does not exist."
    exit 0
fi

if [ "$FORCE" -ne 1 ]; then
    echo
    echo "WARNING: this permanently DELETES the Armada database and runtime state under:"
    echo "    $ARMADA_DIR"
    echo "  - armada.db: all fleets, vessels, captains, missions, voyages, jobs, and more"
    echo "  - docks (git worktrees), repos (clones), logs, settings.json"
    if [ "$WIPE_ALL" -eq 1 ]; then
        echo "  - --all: ALSO the deployed server bin and dashboard"
    fi
    echo
    printf 'Type YES to continue: '
    CONFIRM=""
    read -r CONFIRM || true
    if [ "$CONFIRM" != "YES" ]; then
        echo "[factory-reset] Aborted."
        exit 1
    fi
fi

echo
echo "[factory-reset] Stopping the Armada Admiral..."
# A still-running server can keep writing to (or recreate) the database, leaving the reset incomplete.
if ! stop_servers; then
    echo "ERROR: Armada.Server is still running and could not be stopped. Aborting before deleting state so" >&2
    echo "       the database is not left intact. Stop it manually and re-run factory-reset." >&2
    exit 1
fi

echo "[factory-reset] Deleting database and runtime state..."
rm -f "$ARMADA_DIR"/armada.db "$ARMADA_DIR"/armada.db-shm "$ARMADA_DIR"/armada.db-wal \
      "$ARMADA_DIR"/crash.log "$ARMADA_DIR"/settings.json
rm -rf "$ARMADA_DIR"/docks "$ARMADA_DIR"/repos "$ARMADA_DIR"/logs

if [ "$WIPE_ALL" -eq 1 ]; then
    echo "[factory-reset] Removing deployed server bin and dashboard..."
    rm -rf "${ARMADA_DIR:?}"/bin "${ARMADA_DIR:?}"/dashboard
fi

echo
echo "[factory-reset] Done. Armada state wiped at $ARMADA_DIR."
if wsl_startup_exists armada; then
    echo "Publish and start it factory-fresh: ./scripts/wsl/update-startup.sh"
elif [ "$WIPE_ALL" -eq 1 ]; then
    echo "Publish and register it again: ./scripts/wsl/install-startup.sh"
else
    echo "Start it factory-fresh: ./scripts/wsl/start-armada-server.sh (or ./scripts/wsl/install-startup.sh to start it at login)"
fi
exit 0
