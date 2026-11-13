#!/usr/bin/env bash
# Stop the Armada MCP stdio hosts (dotnet Armada.Helm.dll mcp stdio) that run from a repository checkout, so a
# rebuild can replace their files. Only hosts whose command line names that checkout are stopped (SIGKILL by PID);
# hosts started from the installed armada tool and other checkouts are left alone. Succeeds when none are running.
# WSL 1 version: stops hosts in this distro only; hosts that Windows clients started run on Windows
# (scripts\windows\stop-repo-mcp-hosts.ps1).
#
# Usage: scripts/wsl/stop-repo-mcp-hosts.sh [<repo root>]
#   <repo root>  the checkout to match; defaults to the one this script is in
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
[ "$#" -le 1 ] || { echo "ERROR: Unexpected argument: $2 (run with -h for usage)" >&2; exit 2; }
if [ "$#" -eq 1 ]; then
    [ -d "$1" ] || die "Not a directory: $1"
    REPO_ROOT="$(cd "$1" && pwd)"
fi

MCP_PIDS="$(ps -eo pid=,args= | awk -v repo="$REPO_ROOT" '/Armada\.Helm\.dll mcp stdio/ && index($0, repo) > 0 { print $1 }')"
if [ -z "$MCP_PIDS" ]; then
    echo "[update] No repo-backed MCP stdio hosts found."
    exit 0
fi
for pid in $MCP_PIDS; do
    echo "[update] Stopping MCP stdio host PID $pid..."
    kill -9 "$pid" 2>/dev/null || true
done
