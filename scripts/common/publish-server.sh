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
if ! "${SCRIPT_DIR}/deploy-dashboard.sh"; then
    echo "ERROR: Dashboard deploy failed. The server was published, but ~/.armada/dashboard was not updated." >&2
    echo "       Fix the dashboard build (or install Node.js, or rely on the committed dist/) and re-run." >&2
    exit 1
fi

if [ ! -f "${SERVER_EXE}" ]; then
    echo "ERROR: Published server executable not found at ${SERVER_EXE}" >&2
    exit 1
fi

echo
echo "[publish-server] Completed."
echo "[publish-server] Server executable: ${SERVER_EXE}"
