#!/usr/bin/env bash
# Build, push and pull all three Armada Docker images in turn: build-server.sh, build-dashboard.sh and
# build-proxy.sh, each with the same optional tag. Stops at the first failure.
#
# Usage: build-all.sh [<tag>]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }
case "${1:-}" in -h|--help) usage; exit 0 ;; -*) echo "ERROR: Unexpected argument: $1 (run with -h for usage)" >&2; exit 2 ;; esac
[ "$#" -le 1 ] || { echo "ERROR: Unexpected argument: $2 (run with -h for usage)" >&2; exit 2; }

echo "=== Building Armada server ==="
"${SCRIPT_DIR}/build-server.sh" "$@"

echo "=== Building Armada dashboard ==="
"${SCRIPT_DIR}/build-dashboard.sh" "$@"

echo "=== Building Armada proxy ==="
"${SCRIPT_DIR}/build-proxy.sh" "$@"

echo "=== All Armada images built, pushed to Docker Hub, and pulled into the local registry ==="
