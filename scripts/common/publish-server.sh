#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
PUBLISH_DIR="${HOME}/.armada/bin"
SERVER_EXE="${PUBLISH_DIR}/Armada.Server"

# shellcheck source=scripts/common/resolve-framework.sh
source "${SCRIPT_DIR}/resolve-framework.sh"
armada_resolve_framework "$@"

echo
echo "[publish-server] Publishing Armada.Server (${ARMADA_TARGET_FRAMEWORK}) to ${PUBLISH_DIR}..."
dotnet publish "${REPO_ROOT}/src/Armada.Server" -c Release -f "$ARMADA_TARGET_FRAMEWORK" -o "${PUBLISH_DIR}"

echo
echo "[publish-server] Deploying dashboard assets..."
# Same rule as publish-server.bat: keep going only when a previously deployed dashboard is there to serve.
if ! "${SCRIPT_DIR}/deploy-dashboard.sh"; then
    if [ -f "${HOME}/.armada/dashboard/index.html" ]; then
        echo "[publish-server] WARNING: Dashboard deploy failed. Keeping the previously deployed React dashboard."
    else
        echo "ERROR: Dashboard deploy failed and no deployed React dashboard is available." >&2
        exit 1
    fi
fi

if [ ! -f "${SERVER_EXE}" ]; then
    echo "ERROR: Published server executable not found at ${SERVER_EXE}" >&2
    exit 1
fi

echo
echo "[publish-server] Completed."
echo "[publish-server] Server executable: ${SERVER_EXE}"
