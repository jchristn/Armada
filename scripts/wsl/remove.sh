#!/usr/bin/env bash
# Uninstall the armada CLI (Armada.Helm global tool) and delete the deployed dashboard in ~/.armada/dashboard.
# Succeeds with "nothing to remove" when neither is there. Fails while an Armada server is running (stop it
# with stop-armada-server.sh first). The database, settings and published server in ~/.armada are kept
# (factory-reset.sh deletes them), and a start-at-login registration stays (remove-startup.sh removes it).
# WSL 1 version: removes the install in this distro only; use scripts\windows\remove.bat for the Windows install.
#
# Usage: scripts/wsl/remove.sh [<framework> | -f <framework> | --framework <framework>]
#   The framework is accepted for parity with the other scripts and is not used.
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

# shellcheck source=../common/resolve-framework.sh
. "$REPO_ROOT/scripts/common/resolve-framework.sh"
armada_resolve_framework "$@"
armada_reject_unexpected_args "$@"
wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"

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

# Fails while an Admiral runs in this distro. Usage: refuse_while_running <prefix>
refuse_while_running() {
    local running
    echo "[$1] Checking for a running Armada server..."
    running="$(armada_server_pids | tr '\n' ' ')"
    if [ -n "${running// /}" ]; then
        die "An Armada server is running (PID ${running% }). Stop it with scripts/wsl/stop-armada-server.sh and try again."
    fi
}

# True when the Armada.Helm global tool is installed in this distro.
tool_installed() {
    dotnet tool list --global 2>/dev/null | awk 'tolower($1) == "armada.helm" { found = 1 } END { exit !found }'
}

DASHBOARD_DIR="$HOME/.armada/dashboard"

echo
refuse_while_running remove

REMOVED=0
echo
echo "[remove] Uninstalling the global Armada.Helm tool..."
if tool_installed; then
    dotnet tool uninstall --global Armada.Helm
    REMOVED=1
else
    echo "[remove] Armada.Helm is not installed."
fi

if [ -d "$DASHBOARD_DIR" ]; then
    echo
    echo "[remove] Removing deployed dashboard from $DASHBOARD_DIR"
    rm -rf "$DASHBOARD_DIR"
    REMOVED=1
fi

echo
if [ "$REMOVED" -eq 0 ]; then
    echo "[remove] Armada is not installed; nothing to remove."
else
    echo "[remove] Completed."
fi
if wsl_startup_exists armada; then
    echo "[remove] Armada is still registered to start at Windows login; scripts/wsl/remove-startup.sh removes that."
fi
