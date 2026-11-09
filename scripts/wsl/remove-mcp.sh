#!/usr/bin/env sh
# Removes the Armada MCP server from Claude Code, Codex, Gemini, Cursor, Mux, OpenCode and pi. WSL 1 counterpart of scripts/linux/remove-mcp.sh.
# Windows shims for claude/codex/gemini (npm under /mnt/c) are dropped from the PATH, so only the WSL-side client configs (~/.claude.json, ~/.codex, ~/.gemini, ./.cursor/mcp.json) are changed. For clients that run on Windows, use scripts\windows\remove-mcp.bat from Windows.
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl
# shellcheck source=_wsl-common.sh source-path=SCRIPTDIR
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
wsl_require_cmd dotnet "Install the .NET SDK inside WSL (https://learn.microsoft.com/dotnet/core/install/linux)."
for client in claude codex gemini; do
    if ! command -v "$client" >/dev/null 2>&1; then
        if PATH="$WSL_ORIGINAL_PATH" command -v "$client" >/dev/null 2>&1; then
            wsl_log "Note: '$client' is only installed on the Windows side; its WSL config is still updated, but"
            wsl_log "      the Windows copy is not. Run scripts\\windows\\remove-mcp.bat from Windows for that."
        fi
    fi
done
wsl_log "Cursor on Windows reads its own config; run scripts\\windows\\remove-mcp.bat there if you use it."
wsl_run "$SCRIPT_DIR/../linux/remove-mcp.sh" "$@"
