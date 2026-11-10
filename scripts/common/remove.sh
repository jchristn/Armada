#!/usr/bin/env bash
# Uninstall the armada CLI (Armada.Helm global tool) and delete the deployed dashboard in ~/.armada/dashboard.
# Succeeds with "nothing to remove" when neither is there. Fails while an Armada server is running (stop it
# with stop-armada-server.sh first). The database, settings and published server in ~/.armada are kept
# (factory-reset.sh deletes them), and a start-at-login registration stays (remove-startup.sh removes it).
#
# Usage: remove.sh [<framework> | -f <framework> | --framework <framework>]
#   The framework is accepted for parity with the other scripts and is not used.
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
# Native-library checks (ICU, Avalonia, clipboard); they do nothing where there is no ldconfig (macOS).
# shellcheck source=scripts/common/native-libs.sh
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/native-libs.sh"
native_check_icu

DASHBOARD_DIR="${HOME}/.armada/dashboard"

# True when the Armada.Helm global tool is installed.
tool_installed() {
    dotnet tool list --global 2>/dev/null | awk 'tolower($1) == "armada.helm" { found = 1 } END { exit !found }'
}

echo
echo "[remove] Checking for a running Armada server..."
RUNNING="$("${SCRIPT_DIR}/stop-armada-server.sh" --list | tr '\n' ' ')"
if [ -n "${RUNNING// /}" ]; then
    die "An Armada server is running (PID ${RUNNING% }). Stop it with scripts/${OS_DIR}/stop-armada-server.sh and try again."
fi

REMOVED=0
echo
echo "[remove] Uninstalling the global Armada.Helm tool..."
if tool_installed; then
    dotnet tool uninstall --global Armada.Helm
    REMOVED=1
else
    echo "[remove] Armada.Helm is not installed."
fi

if [ -d "${DASHBOARD_DIR}" ]; then
    echo
    echo "[remove] Removing deployed dashboard from ${DASHBOARD_DIR}"
    rm -rf "${DASHBOARD_DIR}"
    REMOVED=1
fi

echo
if [ "$REMOVED" -eq 0 ]; then
    echo "[remove] Armada is not installed; nothing to remove."
else
    echo "[remove] Completed."
fi
