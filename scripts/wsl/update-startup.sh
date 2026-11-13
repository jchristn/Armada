#!/usr/bin/env bash
# Republish Armada.Server from this checkout and restart the start-at-login deployment that install-startup.sh
# registered: stops the server, publishes into ~/.armada/bin, rewrites the Windows Startup launcher, starts the server
# through it and waits for its health check. Fails when Armada is not registered; run install-startup.sh first.
# WSL 1 version: the registration is the hidden launcher in the Windows Startup folder (see install-startup.sh).
#
# Usage: scripts/wsl/update-startup.sh [<framework> | -f <framework> | --framework <framework>] [-k|--insecure]
#   <framework>  net10.0 (default, or ARMADA_TARGET_FRAMEWORK) or net8.0
#   --insecure   also -k, --no-strict-ssl, --ignore-cert-errors: skip TLS certificate checks for npm/Node only
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
# Install the system packages the headless Admiral (ICU) needs when they are missing.
# This runs before the dotnet check, whose ICU check would stop first. INSTALL_SYSTEM_PACKAGES=0 only reports them.
wsl_install_deps "Armada" icu
wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
wsl_require_cmd curl "Install curl inside WSL (for example: sudo apt install curl)."
wsl_win_exe cmd.exe >/dev/null
wsl_win_exe wscript.exe >/dev/null

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

# Builds the dashboard with the Linux Node.js in this distro (wsl_sanitize_path drops Windows node/npm from the
# PATH) and deploys it into ~/.armada/dashboard. Without Node.js it deploys the dist/ an earlier build left, and
# fails when there is none. Every step is checked explicitly, so callers may run it in a condition.
deploy_dashboard() {
    local dashboard_dir="$REPO_ROOT/src/Armada.Dashboard"
    local dist_dir="$REPO_ROOT/src/Armada.Dashboard/dist"
    local target_dir="$HOME/.armada/dashboard"
    echo
    echo "[deploy-dashboard] Starting dashboard build and deploy"
    [ -f "$dashboard_dir/package.json" ] || die "Dashboard project not found at $dashboard_dir"
    if command -v node >/dev/null 2>&1; then
        wsl_require_cmd npm "Install npm inside WSL (it ships with Node.js: https://nodejs.org)."
        (
            cd "$dashboard_dir" || exit 1
            if [ ! -d node_modules ]; then
                echo "[deploy-dashboard] Installing dependencies..."
                npm install || exit 1
            fi
            echo "[deploy-dashboard] Building..."
            if ! npm run build; then
                # A node_modules installed by Windows npm (a checkout shared with Windows) holds Windows-only native
                # packages; reinstall from the lockfile with Linux npm and try once more.
                [ -f package-lock.json ] || exit 1
                echo "[deploy-dashboard] Initial build failed. Reinstalling dashboard dependencies and retrying..."
                rm -rf node_modules || exit 1
                npm ci || exit 1
                npm run build || exit 1
            fi
        ) || die "The dashboard build failed."
        [ -f "$dist_dir/index.html" ] || die "Dashboard build did not produce dist/index.html"
    elif [ -f "$dist_dir/index.html" ]; then
        echo "[deploy-dashboard] Node.js not found on the Linux PATH; deploying the pre-built dashboard from $dist_dir"
    else
        die "Node.js is not installed in this distro and no pre-built dashboard exists at $dist_dir. Install Node.js inside WSL (https://nodejs.org; a Windows node is not used), or build the dashboard once on a machine that has Node."
    fi
    echo "[deploy-dashboard] Deploying dashboard to $target_dir"
    { rm -rf "$target_dir" && mkdir -p "$target_dir" && cp -R "$dist_dir/." "$target_dir/"; } \
        || die "Failed to deploy the dashboard to $target_dir"
    echo "Dashboard deployed to $target_dir"
}

# Publishes Armada.Server (Release) into ~/.armada/bin and deploys the dashboard; a failed dashboard deploy is only a
# warning when a previously deployed dashboard is there to serve. The caller makes sure no Admiral is running.
publish_server() {
    local publish_dir="$HOME/.armada/bin"
    echo
    echo "[publish-server] Publishing Armada.Server (${ARMADA_TARGET_FRAMEWORK}) to ${publish_dir}..."
    # shellcheck disable=SC2086 # the framework arguments are several words
    dotnet publish "$REPO_ROOT/src/Armada.Server" -c Release $ARMADA_DOTNET_FRAMEWORK_ARGS -o "$publish_dir"

    echo
    echo "[publish-server] Deploying dashboard assets..."
    if ! (deploy_dashboard); then
        if [ -f "$HOME/.armada/dashboard/index.html" ]; then
            echo "[publish-server] WARNING: Dashboard deploy failed. Keeping the previously deployed React dashboard."
        else
            die "Dashboard deploy failed and no deployed React dashboard is available."
        fi
    fi
    [ -f "$publish_dir/Armada.Server" ] || die "Published server executable not found at $publish_dir/Armada.Server"
    # A self-rebuild slot pointer would make the launcher keep running the old slot instead of this publish.
    if [ -f "$publish_dir/current" ]; then
        rm -f "$publish_dir/current"
        echo "[publish-server] Cleared $publish_dir/current, so the start scripts run this publish rather than an older slot."
    fi
}

# Waits up to 30 seconds for <base url>/api/v1/status/health. Usage: wait_healthy <base url>
wait_healthy() {
    local health_url="${1%/}/api/v1/status/health"
    echo
    echo "[healthcheck-server] Waiting for ${health_url}..."
    for _ in $(seq 1 30); do
        if curl -fsS --max-time 3 "$health_url" >/dev/null 2>&1; then
            echo "[healthcheck-server] Healthy."
            return 0
        fi
        sleep 1
    done
    die "Armada server did not become healthy at ${health_url}"
}

# Removes start-at-login entries the scripts/linux scripts (or an earlier scripts/wsl version) may have left: a
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

# What the Windows Startup launcher runs, through /bin/sh -c with ~/.armada as $1: the slot named by bin/current
# when it holds a server (see docs/SERVER_REBUILD.md), else bin/Armada.Server, from ~/.armada, with output appended to
# ~/.armada/logs/wsl-startup-stdout.log and wsl-startup-stderr.log. The VBScript line that carries it cannot hold
# double quotes, so IFS= and set -f stand in for quoting.
ARMADA_LAUNCH='set -f; IFS=; cd $1 || exit 1; mkdir -p logs; exe=$1/bin/Armada.Server; if test -f bin/current; then slot=$(tr -d [:space:] <bin/current); if test ${#slot} -gt 0 && test -f bin/slots/$slot/Armada.Server; then exe=$1/bin/slots/$slot/Armada.Server; fi; fi; exec $exe >>logs/wsl-startup-stdout.log 2>>logs/wsl-startup-stderr.log'

# Writes the Windows Startup launcher for ~/.armada/bin/Armada.Server and starts it through that launcher, so the
# server (and the distro) keep running after this terminal closes. A fresh wsl.exe login has neither this shell's
# sanitized PATH (without it the Windows PATH comes back and captains could run Windows claude/codex/git shims)
# nor its DOTNET_ROOT, so both are passed explicitly.
register_and_start() {
    set -- env "PATH=$PATH"
    if [ -n "${DOTNET_ROOT:-}" ]; then set -- "$@" "DOTNET_ROOT=$DOTNET_ROOT"; fi
    mkdir -p "$HOME/.armada/logs"
    wsl_startup_install armada "$@" /bin/sh -c "$ARMADA_LAUNCH" armada-server "$HOME/.armada"
    echo "[update-startup] Registered $HOME/.armada/bin/Armada.Server to start in WSL ($WSL_DISTRO_NAME) at Windows login."
    echo
    echo "[update-startup] Starting Armada.Server through the launcher..."
    wsl_startup_run armada
}

refuse_product_registration
if ! wsl_startup_exists armada; then
    die "Armada is not registered to start at Windows login. Run scripts/wsl/install-startup.sh first."
fi
remove_linux_startup_entries >/dev/null

echo
echo "[update-startup] Stopping Armada.Server..."
stop_servers || die "Armada.Server is still running and could not be stopped."

echo
echo "[update-startup] Publishing Armada.Server..."
publish_server

echo
register_and_start
wait_healthy "${ARMADA_BASE_URL:-http://localhost:7890}"

echo
echo "[update-startup] Completed."
