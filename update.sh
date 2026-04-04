#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo
echo "[update] Stopping Armada server if it is running..."
armada server stop || true

echo
echo "[update] Stopping repo-backed Armada MCP stdio hosts if they are running..."
mapfile -t MCP_PIDS < <(pgrep -af "Armada\\.Helm\\.dll mcp stdio" | awk -v repo="$SCRIPT_DIR" 'index($0, repo) > 0 { print $1 }' || true)
if [ "${#MCP_PIDS[@]}" -eq 0 ]; then
  echo "[update] No repo-backed MCP stdio hosts found."
else
  for pid in "${MCP_PIDS[@]}"; do
    [ -n "$pid" ] || continue
    echo "[update] Stopping MCP stdio host PID $pid..."
    kill -9 "$pid"
  done
fi

echo
echo "[update] Reinstalling Armada tool and redeploying dashboard..."
"$SCRIPT_DIR/reinstall.sh"

echo
echo "[update] Starting Armada server..."
armada server start
