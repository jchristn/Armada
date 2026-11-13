#!/usr/bin/env bash
# Regenerate the frozen public API surface (V1 readiness W2.1): docs/api-surface-1.0.json (read by the
# E2E.ApiContract test) and docs/API_SURFACE_1.0.md. Boots a throwaway in-process Admiral (temp data directory,
# random free ports; never ~/.armada) through src/Test.Automated. Only regenerate the committed baseline for an
# intended change (see docs/COMPATIBILITY.md).
# WSL 2 version: builds with the Linux dotnet in this distro.
#
# Usage: scripts/wsl2/generate-api-surface.sh [<framework> | -f <framework> | --framework <framework>] [--out <dir>]
#   --out  default: the repository's docs/ directory
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
wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"

mkdir -p "$OUT_DIR"
OUT_DIR="$(cd "$OUT_DIR" && pwd)"

# shellcheck disable=SC2086 # the framework arguments are several words
dotnet run --project "$REPO_ROOT/src/Test.Automated" $ARMADA_DOTNET_FRAMEWORK_ARGS -- --generate-api-surface "$OUT_DIR"
