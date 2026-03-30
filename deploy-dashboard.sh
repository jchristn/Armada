#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DASHBOARD_DIR="${ROOT_DIR}/src/Armada.Dashboard"
DIST_DIR="${DASHBOARD_DIR}/dist"
TARGET_DIR="${HOME}/.armada/dashboard"

echo
echo "[deploy-dashboard] Starting dashboard build and deploy"

if [ ! -f "${DASHBOARD_DIR}/package.json" ]; then
    echo "ERROR: Dashboard project not found at ${DASHBOARD_DIR}"
    exit 1
fi

cd "${DASHBOARD_DIR}"
if [ ! -d "node_modules" ]; then
    echo "[deploy-dashboard] Installing dependencies..."
    npm install
fi

echo "[deploy-dashboard] Building..."
npm run build

if [ ! -f "${DIST_DIR}/index.html" ]; then
    echo "ERROR: Dashboard build did not produce dist/index.html"
    exit 1
fi

echo "[deploy-dashboard] Deploying dashboard to ${TARGET_DIR}"
rm -rf "${TARGET_DIR}"
mkdir -p "${TARGET_DIR}"
cp -R "${DIST_DIR}/." "${TARGET_DIR}/"

echo "Dashboard deployed to ${TARGET_DIR}"
