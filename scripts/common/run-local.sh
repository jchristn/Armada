#!/usr/bin/env bash
# Build and run Armada locally for development: builds the server and Harbor, starts the server in the
# background, waits for it to become healthy, then runs Harbor in the foreground. When Harbor exits (or you
# Ctrl+C), the background server is stopped too.
#
# Usage:
#   run-local.sh [-f <framework>|--framework <framework>|<framework>]
# The framework (e.g. net8.0 or net10.0) is used to BOTH build and run; default net10.0.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

# shellcheck source=scripts/common/resolve-framework.sh
. "${SCRIPT_DIR}/resolve-framework.sh"
armada_resolve_framework "$@"
FRAMEWORK="${ARMADA_TARGET_FRAMEWORK}"

BASE_URL="${ARMADA_BASE_URL:-http://localhost:7890}"
HEALTH_URL="${BASE_URL%/}/api/v1/status/health"

echo "[run-local] Building server + Harbor (framework ${FRAMEWORK})..."
dotnet build "${REPO_ROOT}/src/Armada.Server/Armada.Server.csproj" --framework "${FRAMEWORK}"
dotnet build "${REPO_ROOT}/src/Armada.Harbor/Armada.Harbor.csproj" --framework "${FRAMEWORK}"

echo "[run-local] Starting Armada Server (${FRAMEWORK})..."
dotnet run --project "${REPO_ROOT}/src/Armada.Server" --framework "${FRAMEWORK}" --no-build &
SERVER_PID=$!
trap 'echo "[run-local] Stopping server (pid ${SERVER_PID})..."; kill "${SERVER_PID}" 2>/dev/null || true' EXIT INT TERM

echo "[run-local] Waiting for server health at ${HEALTH_URL}..."
HEALTHY=0
for _ in $(seq 1 30); do
    if ! kill -0 "${SERVER_PID}" 2>/dev/null; then
        echo "[run-local] ERROR: server process exited before becoming healthy." >&2
        exit 1
    fi
    if command -v curl >/dev/null 2>&1 && curl -sf -o /dev/null "${HEALTH_URL}"; then
        HEALTHY=1
        break
    fi
    sleep 1
done

if [ "${HEALTHY}" -eq 1 ]; then
    echo "[run-local] Server healthy."
else
    echo "[run-local] WARNING: server not confirmed healthy after 30s; starting Harbor anyway (it reconnects)."
fi

echo "[run-local] Starting Armada Harbor (${FRAMEWORK})..."
dotnet run --project "${REPO_ROOT}/src/Armada.Harbor" --framework "${FRAMEWORK}" --no-build
