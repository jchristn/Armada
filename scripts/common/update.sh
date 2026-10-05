#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

# shellcheck source=scripts/common/resolve-framework.sh
source "${SCRIPT_DIR}/resolve-framework.sh"
armada_resolve_framework "$@"

HELM_DLL="$REPO_ROOT/src/Armada.Helm/bin/Debug/${ARMADA_TARGET_FRAMEWORK}/Armada.Helm.dll"

# Same order as update.bat: prefer the repo-targeted Helm (the build for the resolved framework, then dotnet run),
# and fall back to the installed armada tool only when dotnet run fails.
run_helm() {
  if [ -f "$HELM_DLL" ]; then
    dotnet "$HELM_DLL" "$@"
    return
  fi

  if dotnet run --project "$REPO_ROOT/src/Armada.Helm" -f "$ARMADA_TARGET_FRAMEWORK" -- "$@"; then
    return 0
  fi

  if command -v armada >/dev/null 2>&1; then
    armada "$@"
    return
  fi

  return 1
}

echo
echo "[update] Stopping repo-backed Armada MCP stdio hosts if they are running..."
# ps -eo pid=,args= works on both Linux and macOS (pgrep -a means "list the full command" on Linux but
# "include ancestors" on macOS), and a plain string avoids mapfile, which the macOS system bash 3.2 lacks.
MCP_PIDS="$(ps -eo pid=,args= | awk -v repo="$REPO_ROOT" '/Armada\.Helm\.dll mcp stdio/ && index($0, repo) > 0 { print $1 }' || true)"
if [ -z "$MCP_PIDS" ]; then
  echo "[update] No repo-backed MCP stdio hosts found."
else
  for pid in $MCP_PIDS; do
    [ -n "$pid" ] || continue
    echo "[update] Stopping MCP stdio host PID $pid..."
    kill -9 "$pid"
  done
fi

echo
echo "[update] Stopping Armada server if it is running..."
run_helm server stop || true

echo
echo "[update] Reinstalling Armada tool and redeploying dashboard..."
"$SCRIPT_DIR/reinstall.sh"

echo
echo "[update] Starting Armada server..."
run_helm server start
