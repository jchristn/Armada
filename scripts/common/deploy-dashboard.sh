#!/usr/bin/env bash
# Build the React dashboard (src/Armada.Dashboard) with npm and deploy it into ~/.armada/dashboard, where the
# server serves it from. Without Node.js it deploys the dist/ left by an earlier build, and fails when there is none.
# When the build fails and a package-lock.json exists, it reinstalls node_modules (npm ci) and builds once more.
#
# Usage: deploy-dashboard.sh [<framework> | -f <framework> | --framework <framework>] [-k|--insecure]
#   The framework is accepted (so callers can forward their arguments) and is not used.
#   --insecure   also -k, --no-strict-ssl, --ignore-cert-errors: skip TLS certificate checks for npm/Node only
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }
case "${1:-}" in -h|--help) usage; exit 0 ;; esac

DASHBOARD_DIR="${REPO_ROOT}/src/Armada.Dashboard"
DIST_DIR="${DASHBOARD_DIR}/dist"
TARGET_DIR="${HOME}/.armada/dashboard"

# Honors an "ignore TLS certificate" flag (-k / --insecure) for npm behind an SSL-inspecting proxy.
# shellcheck source=scripts/common/resolve-framework.sh
. "${SCRIPT_DIR}/resolve-framework.sh"
armada_resolve_framework "$@"
armada_reject_unexpected_args "$@"

echo
echo "[deploy-dashboard] Starting dashboard build and deploy"

if [ ! -f "${DASHBOARD_DIR}/package.json" ]; then
    echo "ERROR: Dashboard project not found at ${DASHBOARD_DIR}" >&2
    exit 1
fi

# Building the dashboard requires Node.js. When Node is unavailable, fall back to the pre-built
# dist left by an earlier build (dist/ is not committed) so install still works without Node.
if command -v node >/dev/null 2>&1; then
    if ! command -v npm >/dev/null 2>&1; then
        echo "ERROR: Node.js is installed but npm is not on PATH. Install npm (it ships with Node.js: https://nodejs.org)." >&2
        exit 1
    fi
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
        echo "ERROR: Dashboard build did not produce dist/index.html" >&2
        exit 1
    fi
elif [ -f "${DIST_DIR}/index.html" ]; then
    echo "[deploy-dashboard] Node.js not found on PATH; deploying the pre-built dashboard from ${DIST_DIR}"
else
    echo "ERROR: Node.js is not installed and no pre-built dashboard exists at ${DIST_DIR}." >&2
    echo "Install Node.js (https://nodejs.org) or build the dashboard once on a machine that has Node." >&2
    exit 1
fi

echo "[deploy-dashboard] Deploying dashboard to ${TARGET_DIR}"
rm -rf "${TARGET_DIR}"
mkdir -p "${TARGET_DIR}"
cp -R "${DIST_DIR}/." "${TARGET_DIR}/"

echo "Dashboard deployed to ${TARGET_DIR}"
