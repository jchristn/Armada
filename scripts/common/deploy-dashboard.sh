#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
DASHBOARD_DIR="${REPO_ROOT}/src/Armada.Dashboard"
DIST_DIR="${DASHBOARD_DIR}/dist"
TARGET_DIR="${HOME}/.armada/dashboard"

# Honor an "ignore TLS certificate" flag (-k / --insecure) for npm behind an SSL-inspecting proxy.
# shellcheck source=scripts/common/resolve-insecure.sh
. "${SCRIPT_DIR}/resolve-insecure.sh"
armada_resolve_insecure "$@"

echo
echo "[deploy-dashboard] Starting dashboard build and deploy"

if [ ! -f "${DASHBOARD_DIR}/package.json" ]; then
    echo "ERROR: Dashboard project not found at ${DASHBOARD_DIR}"
    exit 1
fi

# Building the dashboard requires Node.js. When Node is unavailable, fall back to the pre-built
# dist that ships in the repository so install still works on a machine without Node installed.
if command -v node >/dev/null 2>&1; then
    cd "${DASHBOARD_DIR}"
    if [ ! -d "node_modules" ]; then
        echo "[deploy-dashboard] Installing dependencies..."
        npm install
    fi

    echo "[deploy-dashboard] Building..."
    if ! npm run build; then
        # A stale or foreign node_modules (for example one installed on another OS) breaks the
        # toolchain; reinstall from the lockfile and try once more, as deploy-dashboard.bat does.
        if [ -f "package-lock.json" ]; then
            echo "[deploy-dashboard] Initial build failed. Reinstalling dashboard dependencies and retrying..."
            rm -rf node_modules
            npm ci
            npm run build
        else
            exit 1
        fi
    fi

    if [ ! -f "${DIST_DIR}/index.html" ]; then
        echo "ERROR: Dashboard build did not produce dist/index.html"
        exit 1
    fi
elif [ -f "${DIST_DIR}/index.html" ]; then
    echo "[deploy-dashboard] Node.js not found on PATH; deploying the pre-built dashboard from ${DIST_DIR}"
else
    echo "ERROR: Node.js is not installed and no pre-built dashboard exists at ${DIST_DIR}."
    echo "Install Node.js (https://nodejs.org) or build the dashboard once on a machine that has Node."
    exit 1
fi

echo "[deploy-dashboard] Deploying dashboard to ${TARGET_DIR}"
rm -rf "${TARGET_DIR}"
mkdir -p "${TARGET_DIR}"
cp -R "${DIST_DIR}/." "${TARGET_DIR}/"

echo "Dashboard deployed to ${TARGET_DIR}"
