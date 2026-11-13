#!/usr/bin/env bash
# Wait up to 30 seconds for the Armada server's health endpoint (<base url>/api/v1/status/health) to answer.
# Exits 0 once it does and 1 when it never does.
# WSL 2 version: under NAT networking, when no base URL is given (no argument, and none of ARMADA_BASE_URL,
# ARMADA_SERVER_URL and ARMADA_URL is set) and nothing answers at localhost inside this distro but an Admiral on the
# Windows host does, the Windows host address is used (WSL_NO_HOST_REWRITE=1 turns that off; mirrored networking
# needs no rewrite).
#
# Usage: scripts/wsl2/healthcheck-server.sh [<framework> | -f <framework> | --framework <framework>] [<base url>]
#   <base url>   default ARMADA_BASE_URL, else http://localhost:7890
#   A leading framework is accepted and ignored, as in healthcheck-server.bat.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "ERROR: $*" >&2; exit 1; }
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }

# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl2
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
shift "$ARMADA_FRAMEWORK_ARGS_CONSUMED"
if [ "$#" -gt 1 ]; then
    echo "ERROR: Unexpected argument: $2 (run with -h for usage)" >&2
    exit 2
fi
wsl_require_cmd curl "Install curl inside WSL (for example: sudo apt install curl)."

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

if [ "$#" -eq 1 ]; then
    BASE_URL="$1"
else
    BASE_URL="${ARMADA_BASE_URL:-http://localhost:7890}"
    # Only when no URL was chosen anywhere: try the Windows host for an Admiral running on Windows.
    if [ -z "${ARMADA_BASE_URL:-}${ARMADA_SERVER_URL:-}${ARMADA_URL:-}" ]; then
        BASE_URL="$(wsl_resolve_local_url "$BASE_URL")"
    fi
fi
wait_healthy "$BASE_URL"
