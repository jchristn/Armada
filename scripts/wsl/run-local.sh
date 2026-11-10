#!/usr/bin/env bash
# Build and run Armada locally for development: builds the server and Harbor, builds and deploys the dashboard
# into ~/.armada/dashboard, starts the server in the background, waits for it to become healthy, then runs Harbor
# in the foreground. When Harbor exits (or you press Ctrl+C), the background server is stopped too.
# Set ARMADA_SKIP_DASHBOARD=1 to skip the dashboard build and keep the currently deployed dashboard.
# WSL 1 version: Harbor needs an X server on Windows with DISPLAY set. WSL 1 shares the Windows network.
#
# Usage: scripts/wsl/run-local.sh [<framework> | -f <framework> | --framework <framework>] [-k|--insecure]
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
FRAMEWORK="$ARMADA_TARGET_FRAMEWORK"
wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
wsl_require_cmd curl "Install curl inside WSL (for example: sudo apt install curl)."
# Harbor is a desktop (Avalonia) app. WSL 1 has no WSLg, so it needs an X server on Windows and DISPLAY set.
if [ -z "${DISPLAY:-}" ]; then
    die "WSL 1 has no WSLg, so it cannot show Armada Harbor. Run scripts\\windows\\run-harbor.bat from Windows, move this distro to WSL 2 (wsl --set-version ${WSL_DISTRO_NAME:-<distro>} 2) and use scripts/wsl2, or start an X server on Windows and set DISPLAY."
fi

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

BASE_URL="${ARMADA_BASE_URL:-http://localhost:7890}"
HEALTH_URL="${BASE_URL%/}/api/v1/status/health"

echo "[run-local] Building server + Harbor (framework ${FRAMEWORK})..."
# shellcheck disable=SC2086 # the framework arguments are several words
dotnet build "$REPO_ROOT/src/Armada.Server/Armada.Server.csproj" $ARMADA_DOTNET_FRAMEWORK_ARGS
# shellcheck disable=SC2086 # the framework arguments are several words
dotnet build "$REPO_ROOT/src/Armada.Harbor/Armada.Harbor.csproj" $ARMADA_DOTNET_FRAMEWORK_ARGS

if [ "${ARMADA_SKIP_DASHBOARD:-0}" = "1" ]; then
    echo "[run-local] Skipping dashboard build (ARMADA_SKIP_DASHBOARD=1)."
else
    deploy_dashboard
fi

echo "[run-local] Starting Armada Server (${FRAMEWORK})..."
dotnet run --project "$REPO_ROOT/src/Armada.Server" --framework "$FRAMEWORK" --no-build &
SERVER_PID=$!
trap 'echo "[run-local] Stopping server (pid ${SERVER_PID})..."; kill "${SERVER_PID}" 2>/dev/null || true' EXIT INT TERM

echo "[run-local] Waiting for server health at ${HEALTH_URL}..."
HEALTHY=0
for _ in $(seq 1 30); do
    if ! kill -0 "$SERVER_PID" 2>/dev/null; then
        echo "[run-local] ERROR: server process exited before becoming healthy." >&2
        exit 1
    fi
    if curl -sf --max-time 3 -o /dev/null "$HEALTH_URL"; then
        HEALTHY=1
        break
    fi
    sleep 1
done

if [ "$HEALTHY" -eq 1 ]; then
    echo "[run-local] Server healthy."
else
    echo "[run-local] WARNING: server not confirmed healthy after 30s; starting Harbor anyway (it reconnects)."
fi

echo "[run-local] Starting Armada Harbor (${FRAMEWORK})..."
dotnet run --project "$REPO_ROOT/src/Armada.Harbor" --framework "$FRAMEWORK" --no-build
