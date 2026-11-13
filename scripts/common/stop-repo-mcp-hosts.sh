#!/usr/bin/env bash
# Stop the Armada MCP stdio hosts (dotnet Armada.Helm.dll mcp stdio) that run from a repository checkout, so a
# rebuild can replace their files. Only hosts whose command line names that checkout are stopped (SIGKILL by PID);
# hosts started from the installed armada tool and other checkouts are left alone. Succeeds when none are running.
#
# Usage: stop-repo-mcp-hosts.sh [<repo root>]
#   <repo root>  the checkout to match; defaults to the one this script is in
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
die() { echo "ERROR: $*" >&2; exit 1; }
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }
case "${1:-}" in -h|--help) usage; exit 0 ;; esac
[ "$#" -le 1 ] || { echo "ERROR: Unexpected argument: $2 (run with -h for usage)" >&2; exit 2; }
if [ "$#" -eq 1 ]; then
    [ -d "$1" ] || die "Not a directory: $1"
    REPO_ROOT="$(cd "$1" && pwd)"
fi

# ps -eo pid=,args= works on both Linux and macOS (pgrep -a means "list the full command" on Linux but
# "include ancestors" on macOS), and a plain string avoids mapfile, which the macOS system bash 3.2 lacks.
MCP_PIDS="$(ps -eo pid=,args= | awk -v repo="$REPO_ROOT" '/Armada\.Helm\.dll mcp stdio/ && index($0, repo) > 0 { print $1 }')"
if [ -z "$MCP_PIDS" ]; then
    echo "[update] No repo-backed MCP stdio hosts found."
    exit 0
fi
for pid in $MCP_PIDS; do
    echo "[update] Stopping MCP stdio host PID $pid..."
    kill -9 "$pid" 2>/dev/null || true
done
