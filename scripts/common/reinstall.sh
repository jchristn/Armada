#!/usr/bin/env bash
# Uninstall the armada CLI (Armada.Helm global tool) when it is installed, then build, redeploy the dashboard
# and install it again from this checkout. Succeeds when Armada.Helm was not installed. Fails while an Armada
# server is running (stop it with stop-armada-server.sh first).
#
# Usage: reinstall.sh [<framework> | -f <framework> | --framework <framework>] [-k|--insecure]
#   <framework>  net10.0 (default, or ARMADA_TARGET_FRAMEWORK) or net8.0
#   --insecure   also -k, --no-strict-ssl, --ignore-cert-errors: skip TLS certificate checks for npm/Node only
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# The per-OS directory named in messages; the scripts/linux and scripts/macos wrappers set it.
OS_DIR="${ARMADA_SCRIPT_OS:-$([ "$(uname -s)" = Darwin ] && echo macos || echo linux)}"
die() { echo "ERROR: $*" >&2; exit 1; }
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }
case "${1:-}" in -h|--help) usage; exit 0 ;; esac

# shellcheck source=scripts/common/resolve-framework.sh
source "${SCRIPT_DIR}/resolve-framework.sh"
armada_resolve_framework "$@"
armada_reject_unexpected_args "$@"

command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download"

# True when the Armada.Helm global tool is installed.
tool_installed() {
    dotnet tool list --global 2>/dev/null | awk 'tolower($1) == "armada.helm" { found = 1 } END { exit !found }'
}

echo
echo "[reinstall] Checking for a running Armada server..."
RUNNING="$("${SCRIPT_DIR}/stop-armada-server.sh" --list | tr '\n' ' ')"
if [ -n "${RUNNING// /}" ]; then
    die "An Armada server is running (PID ${RUNNING% }). Stop it with scripts/${OS_DIR}/stop-armada-server.sh and try again."
fi

echo
echo "[reinstall] Removing the existing global Armada.Helm tool..."
if tool_installed; then
    dotnet tool uninstall --global Armada.Helm
else
    echo "[reinstall] Armada.Helm is not installed; continuing."
fi

echo
echo "[reinstall] Running fresh install..."
"${SCRIPT_DIR}/install.sh" "$@"
