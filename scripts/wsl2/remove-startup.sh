#!/usr/bin/env bash
# Unregister Armada from starting at Windows login, then stop the running server: deletes the Windows Startup launcher
# that install-startup.sh writes, plus any systemd user unit or desktop autostart entry left by the scripts/linux
# version. Succeeds with "nothing to remove" when none exists. The published files in ~/.armada stay.
# WSL 2 version: stops the server in this distro only.
#
# Usage: scripts/wsl2/remove-startup.sh [<framework> | -f <framework> | --framework <framework>]
#   The framework is accepted for parity with the other scripts and is not used.
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

# shellcheck source=../common/resolve-framework.sh
. "$REPO_ROOT/scripts/common/resolve-framework.sh"
armada_resolve_framework "$@"
armada_reject_unexpected_args "$@"

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

# Removes start-at-login entries the scripts/linux scripts (or an earlier scripts/wsl2 version) may have left: a
# systemd user unit and a desktop autostart entry. Neither runs at Windows login, and either could start a second
# Admiral when the distro boots. Prints 1 when it removed something, 0 otherwise.
remove_linux_startup_entries() {
    local unit="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user/armada.service"
    local desktop="${XDG_CONFIG_HOME:-$HOME/.config}/autostart/armada.desktop"
    local removed=0
    if [ -f "$unit" ]; then
        if command -v systemctl >/dev/null 2>&1; then
            systemctl --user disable --now armada.service >/dev/null 2>&1 || true
        fi
        rm -f "$unit"
        if command -v systemctl >/dev/null 2>&1; then
            systemctl --user daemon-reload >/dev/null 2>&1 || true
        fi
        echo "[startup] Removed the systemd user unit $unit." >&2
        removed=1
    fi
    if [ -f "$desktop" ]; then
        rm -f "$desktop"
        echo "[startup] Removed the desktop autostart entry $desktop." >&2
        removed=1
    fi
    echo "$removed"
}

# "armada-server --install-service" registers the Admiral itself: ~/.config/systemd/user/armada.service running
# "Armada.Server --run-service", or /etc/systemd/system/armada.service as root. Two registrations would start two
# Admirals on the same ports, so the startup scripts refuse to work next to one (the same rule on every OS).
refuse_product_registration() {
    local unit="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user/armada.service"
    if [ -f "$unit" ] && grep -q -- '--run-service' "$unit"; then
        die "Armada is registered by 'armada-server --install-service' ($unit), which runs its own Admiral. Remove that first with: $HOME/.armada/bin/Armada.Server --uninstall-service (or armada-server --uninstall-service), then run this again."
    fi
    if [ -f /etc/systemd/system/armada.service ]; then
        die "Armada is registered as a system service by 'armada-server --install-service' (/etc/systemd/system/armada.service). Remove that first with: sudo armada-server --uninstall-service (or sudo $HOME/.armada/bin/Armada.Server --uninstall-service), then run this again."
    fi
}

refuse_product_registration
REMOVED=0
if wsl_startup_remove armada; then
    REMOVED=1
fi
if [ "$(remove_linux_startup_entries)" = 1 ]; then
    REMOVED=1
fi
if [ "$REMOVED" -eq 0 ]; then
    echo "[remove-startup] Armada is not registered to start at login; nothing to remove."
    exit 0
fi
echo "[remove-startup] Unregistered Armada from starting at Windows login."

echo
echo "[remove-startup] Stopping Armada.Server..."
stop_servers || die "Armada.Server is still running and could not be stopped."

echo
echo "[remove-startup] Completed."
