#!/usr/bin/env bash
# Remove the Armada MCP server from Claude Code, Codex, Gemini, Cursor and (when present) Mux, OpenCode and pi,
# by running "armada mcp remove --yes" from this checkout (dotnet run of src/Armada.Helm).
#
# Usage: remove-mcp.sh [<framework> | -f <framework> | --framework <framework>] [-k|--insecure]
#   <framework>  net10.0 (default, or ARMADA_TARGET_FRAMEWORK) or net8.0
#   --insecure   also -k, --no-strict-ssl, --ignore-cert-errors: skip TLS certificate checks for npm/Node only
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

die() { echo "ERROR: $*" >&2; exit 1; }
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }
case "${1:-}" in -h|--help) usage; exit 0 ;; esac

# shellcheck source=scripts/common/resolve-framework.sh
source "${SCRIPT_DIR}/resolve-framework.sh"
armada_resolve_framework "$@"
armada_reject_unexpected_args "$@"

command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download"
# Native-library checks (ICU, Avalonia, clipboard); they do nothing where there is no ldconfig (macOS).
# shellcheck source=scripts/common/native-libs.sh
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/native-libs.sh"
native_check_icu

echo
echo "[remove-mcp] Removing Armada MCP for Claude Code, Codex, Gemini, and Cursor (${ARMADA_TARGET_FRAMEWORK})..."
dotnet run --project "$REPO_ROOT/src/Armada.Helm" $ARMADA_DOTNET_FRAMEWORK_ARGS -- mcp remove --yes

echo
echo "[remove-mcp] Completed."
