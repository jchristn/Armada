#!/usr/bin/env bash
# Uninstall the armada CLI (Armada.Helm global tool) when it is installed, then build, redeploy the dashboard
# and install it again from this checkout. Succeeds when Armada.Helm was not installed. Fails while an Armada
# server is running (stop it with stop-armada-server.sh first).
# WSL 1 version: uses the Linux dotnet, node and npm in this distro (never the Windows ones) and reinstalls in this
# distro only; use scripts\windows\reinstall.bat for the Windows install.
#
# Usage: scripts/wsl/reinstall.sh [<framework> | -f <framework> | --framework <framework>] [-k|--insecure]
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
# Create ~/.dotnet/tools first so wsl_sanitize_path puts it on PATH for the new armada command.
mkdir -p "$HOME/.dotnet/tools"
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

# Deploys the dashboard, builds the solution, packs Armada.Helm and installs it as a global tool in this distro.
install_tool() {
    echo
    echo "[install] Deploying dashboard..."
    deploy_dashboard

    echo
    echo "[install] Building Armada solution (${ARMADA_TARGET_FRAMEWORK})..."
    # shellcheck disable=SC2086 # the framework arguments are several words
    dotnet build "$REPO_ROOT/src/Armada.sln" $ARMADA_DOTNET_MSBUILD_FRAMEWORK_ARGS

    echo
    echo "[install] Packing Armada.Helm (${ARMADA_TARGET_FRAMEWORK})..."
    # shellcheck disable=SC2086 # the framework arguments are several words
    dotnet pack "$REPO_ROOT/src/Armada.Helm" $ARMADA_DOTNET_MSBUILD_FRAMEWORK_ARGS -o "$REPO_ROOT/src/nupkg"

    echo
    echo "[install] Installing Armada.Helm as a global tool..."
    dotnet tool install --global --add-source "$REPO_ROOT/src/nupkg" Armada.Helm

    # dotnet tool install --global puts armada in ~/.dotnet/tools; a user-local .NET in ~/.dotnet also needs
    # DOTNET_ROOT, or new shells cannot run it.
    case ":$WSL_ORIGINAL_PATH:" in
        *":$HOME/.dotnet/tools:"*) ;;
        *)
            echo "[install] Note: $HOME/.dotnet/tools is not on your PATH, so new shells will not find 'armada'." >&2
            echo "[install] Add this line to ~/.bashrc (or ~/.profile): export PATH=\"\$PATH:\$HOME/.dotnet/tools\"" >&2
            ;;
    esac
    if [ -z "$WSL_ORIGINAL_DOTNET_ROOT" ] && [ "$(command -v dotnet)" = "$HOME/.dotnet/dotnet" ]; then
        echo "[install] Note: .NET is installed in ~/.dotnet, so 'armada' also needs DOTNET_ROOT. Add this line to ~/.bashrc (or ~/.profile): export DOTNET_ROOT=\"\$HOME/.dotnet\"" >&2
    fi
    echo "[install] This installs armada in this distro only; use scripts\\windows\\install.bat for Windows."
}

echo
refuse_while_running reinstall

echo
echo "[reinstall] Removing the existing global Armada.Helm tool..."
if tool_installed; then
    dotnet tool uninstall --global Armada.Helm
else
    echo "[reinstall] Armada.Helm is not installed; continuing."
fi

echo
echo "[reinstall] Running fresh install..."
install_tool

echo
echo "[install] Completed."
