#!/usr/bin/env sh
# Registers the Armada MCP server with Claude Code, Codex, Gemini, Cursor and (when present) Mux, OpenCode and pi. WSL 2 counterpart of scripts/linux/install-mcp.sh.
# Windows shims for claude/codex/gemini (npm under /mnt/c) are dropped from the PATH, so only the WSL-side client configs (~/.claude.json, ~/.codex, ~/.gemini, ./.cursor/mcp.json) are changed. For clients that run on Windows, use scripts\windows\install-mcp.bat from Windows.
# Under WSL 2 NAT networking, when no Admiral answers on localhost inside the distro but one on Windows does,
# ARMADA_SERVER_URL is set to the Windows host so the clients point at it (that needs ARMADA_TOKEN).
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl2
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
            wsl_log "      the Windows copy is not. Run scripts\\windows\\install-mcp.bat from Windows for that."
        fi
    fi
done
if [ -z "${ARMADA_SERVER_URL:-}" ] && [ -z "${ARMADA_URL:-}" ]; then
    LOCAL_URL="${ARMADA_BASE_URL:-http://localhost:7890}"
    RESOLVED_URL="$(wsl_resolve_local_url "$LOCAL_URL")"
    if [ "$RESOLVED_URL" != "$LOCAL_URL" ]; then
        ARMADA_SERVER_URL="$RESOLVED_URL"; export ARMADA_SERVER_URL
        [ -n "${ARMADA_TOKEN:-}" ] || wsl_log "Note: an Admiral on the Windows host needs a bearer token; set ARMADA_TOKEN and re-run if this fails."
    fi
fi
wsl_log "Cursor on Windows reads its own config; run scripts\\windows\\install-mcp.bat there if you use it."
wsl_run "$SCRIPT_DIR/../linux/install-mcp.sh" "$@"
