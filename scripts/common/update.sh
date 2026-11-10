#!/usr/bin/env bash
# Update a global-tool install from this checkout: stop the repo-backed MCP stdio hosts, stop the Armada server
# (armada server stop, then any Admiral left, by PID), reinstall the armada CLI and redeploy the dashboard
# (reinstall.sh), then start the server again with armada server start. This is not the start-at-login update;
# for that deployment use update-startup.sh.
#
# Usage: update.sh [<framework> | -f <framework> | --framework <framework>] [-k|--insecure]
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

HELM_DLL="$REPO_ROOT/src/Armada.Helm/bin/Debug/${ARMADA_TARGET_FRAMEWORK}/Armada.Helm.dll"

# Same order as update.bat: prefer the repo-targeted Helm (the build for the resolved framework, then dotnet run),
# and fall back to the installed armada tool only when dotnet run fails.
run_helm() {
  if [ -f "$HELM_DLL" ]; then
    dotnet "$HELM_DLL" "$@"
    return
  fi

  if dotnet run --project "$REPO_ROOT/src/Armada.Helm" $ARMADA_DOTNET_FRAMEWORK_ARGS -- "$@"; then
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
"$SCRIPT_DIR/stop-repo-mcp-hosts.sh" "$REPO_ROOT"

echo
echo "[update] Stopping Armada server if it is running..."
run_helm server stop || true

echo
echo "[update] Stopping every remaining Armada.Server process..."
"$SCRIPT_DIR/stop-armada-server.sh" || die "Armada.Server is still running and could not be stopped."

echo
echo "[update] Reinstalling Armada tool and redeploying dashboard..."
"$SCRIPT_DIR/reinstall.sh" $ARMADA_FORWARD_FRAMEWORK_ARGS

echo
echo "[update] Starting Armada server..."
run_helm server start
