#!/usr/bin/env bash
# Remove the Armada MCP server from Claude Code, Codex, Gemini, Cursor and (when present) Mux, OpenCode and pi,
# by running "armada mcp remove --yes" from this checkout (dotnet run of src/Armada.Helm).
# WSL 1 version: changes the client configs in this distro only. Clients that run on Windows read their own
# configs: run scripts\windows\remove-mcp.bat from Windows for those.
#
# Usage: scripts/wsl/remove-mcp.sh [<framework> | -f <framework> | --framework <framework>] [-k|--insecure]
#   <framework>  net10.0 (default, or ARMADA_TARGET_FRAMEWORK) or net8.0
#   --insecure   also -k, --no-strict-ssl, --ignore-cert-errors: skip TLS certificate checks for npm/Node only
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "ERROR: $*" >&2; exit 1; }
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }

# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl
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
armada_reject_unexpected_args "$@"
wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"

for client in claude codex gemini; do
    if ! command -v "$client" >/dev/null 2>&1; then
        if PATH="$WSL_ORIGINAL_PATH" command -v "$client" >/dev/null 2>&1 || PATH="$WSL_ORIGINAL_PATH" command -v "$client.exe" >/dev/null 2>&1; then
            echo "[remove-mcp] Note: '$client' is only installed on the Windows side; its Windows config is not changed." >&2
        fi
    fi
done

echo
echo "[remove-mcp] Removing Armada MCP for Claude Code, Codex, Gemini, and Cursor (${ARMADA_TARGET_FRAMEWORK})..."
# shellcheck disable=SC2086 # the framework arguments are several words
dotnet run --project "$REPO_ROOT/src/Armada.Helm" $ARMADA_DOTNET_FRAMEWORK_ARGS -- mcp remove --yes

echo
echo "[remove-mcp] Completed."
echo "[remove-mcp] Clients running on Windows need scripts\\windows\\remove-mcp.bat, run from Windows."
