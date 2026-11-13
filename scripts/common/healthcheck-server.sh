#!/usr/bin/env bash
# Wait up to 30 seconds for the Armada server's health endpoint (<base url>/api/v1/status/health) to answer.
# Exits 0 once it does and 1 when it never does.
#
# Usage: healthcheck-server.sh [<framework> | -f <framework> | --framework <framework>] [<base url>]
#   <base url>   default ARMADA_BASE_URL, else http://localhost:7890
#   A leading framework is accepted and ignored, as in healthcheck-server.bat.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }
case "${1:-}" in -h|--help) usage; exit 0 ;; esac

# shellcheck source=scripts/common/resolve-framework.sh
. "${SCRIPT_DIR}/resolve-framework.sh"
armada_resolve_framework "$@"
shift "${ARMADA_FRAMEWORK_ARGS_CONSUMED}"
if [ "$#" -gt 1 ]; then
    echo "ERROR: Unexpected argument: $2 (run with -h for usage)" >&2
    exit 2
fi

BASE_URL="${1:-${ARMADA_BASE_URL:-http://localhost:7890}}"
BASE_URL="${BASE_URL%/}"
HEALTH_URL="${BASE_URL}/api/v1/status/health"

if ! command -v curl >/dev/null 2>&1; then
    echo "ERROR: curl is required for health checks. Install curl with your package manager." >&2
    exit 1
fi

echo
echo "[healthcheck-server] Waiting for ${HEALTH_URL}..."

for _ in $(seq 1 30); do
    if curl -fsS --max-time 3 "${HEALTH_URL}" >/dev/null 2>&1; then
        echo "[healthcheck-server] Healthy."
        exit 0
    fi

    sleep 1
done

echo "ERROR: Armada server did not become healthy at ${HEALTH_URL}" >&2
exit 1
