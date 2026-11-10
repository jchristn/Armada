#!/usr/bin/env bash
# =====================================================================
# generate-api-surface.sh -- regenerate the frozen public API surface
# (V1 readiness W2.1): docs/api-surface-1.0.json (read by the E2E.ApiContract
# test) and docs/API_SURFACE_1.0.md (the human-readable view).
#
# Boots a throwaway in-process Admiral (temp data directory, random free
# ports; never ~/.armada) through src/Test.Automated and records the REST
# routes with their OpenAPI metadata and declared authorization, the MCP
# tools with their input schemas, the WebSocket contract, the Helm CLI
# command model, and the ArmadaSettings keys.
#
# Usage:
#   generate-api-surface.sh [-f|--framework net10.0] [--out <dir>]
#
# --out defaults to the repository's docs/ directory. Only regenerate the
# committed baseline for an intended change: additions, or a breaking change
# in a major release (see docs/COMPATIBILITY.md).
# =====================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "ERROR: $*" >&2; exit 1; }
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }
case "${1:-}" in -h|--help) usage; exit 0 ;; esac
# shellcheck source=resolve-framework.sh
. "$SCRIPT_DIR/resolve-framework.sh"
armada_resolve_framework "$@"
shift "$ARMADA_FRAMEWORK_ARGS_CONSUMED"
command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download"

OUT_DIR="$REPO_ROOT/docs"
while [ $# -gt 0 ]; do
    case "$1" in
        --out)
            [ -n "${2:-}" ] || { echo "ERROR: --out needs a directory" >&2; exit 2; }
            OUT_DIR="$2"
            shift 2
            ;;
        *)
            echo "ERROR: unknown argument $1 (run with -h for usage)" >&2
            exit 2
            ;;
    esac
done

mkdir -p "$OUT_DIR"
OUT_DIR="$(cd "$OUT_DIR" && pwd)"

dotnet run --project "$REPO_ROOT/src/Test.Automated" $ARMADA_DOTNET_FRAMEWORK_ARGS -- --generate-api-surface "$OUT_DIR"
