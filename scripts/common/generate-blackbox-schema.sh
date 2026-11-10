#!/usr/bin/env bash
# =====================================================================
# generate-blackbox-schema.sh -- regenerate docs/blackbox-record.schema.json,
# the JSON Schema (draft 2020-12) of the black box record.json and the entry,
# summary, object, chunk, manifest and event payload shapes, from the C#
# models (BlackBoxSchemaGenerator). The Models.BlackBoxSchema test fails when
# the committed file drifts from the models; run this after a model change.
#
# Usage: scripts/common/generate-blackbox-schema.sh [net10.0 | -f net10.0 | --framework net10.0] [--out <dir>]
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
# Native-library checks (ICU, Avalonia, clipboard); they do nothing where there is no ldconfig (macOS).
# shellcheck source=scripts/common/native-libs.sh
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/native-libs.sh"
native_check_icu

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

dotnet run --project "$REPO_ROOT/src/Test.Automated" $ARMADA_DOTNET_FRAMEWORK_ARGS -- --generate-blackbox-schema "$OUT_DIR"
