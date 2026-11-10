#!/usr/bin/env bash
# Publish Armada.Server (Release) into ~/.armada/bin and deploy the dashboard into ~/.armada/dashboard. A failed
# dashboard deploy is only a warning when a previously deployed dashboard is there to serve. Fails while an Armada
# server is running, so a running binary is never overwritten (stop it with stop-armada-server.sh first). Clears the
# self-rebuild slot pointer ~/.armada/bin/current, so the start scripts run the new publish.
#
# Usage: publish-server.sh [<framework> | -f <framework> | --framework <framework>] [-k|--insecure]
#   <framework>  net10.0 (default, or ARMADA_TARGET_FRAMEWORK) or net8.0
#   --insecure   also -k, --no-strict-ssl, --ignore-cert-errors: skip TLS certificate checks for npm/Node only
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
PUBLISH_DIR="${HOME}/.armada/bin"
SERVER_EXE="${PUBLISH_DIR}/Armada.Server"
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

echo
echo "[publish-server] Checking for a running Armada server..."
RUNNING="$("${SCRIPT_DIR}/stop-armada-server.sh" --list | tr '\n' ' ')"
if [ -n "${RUNNING// /}" ]; then
    die "An Armada server is running (PID ${RUNNING% }). Stop it with scripts/${OS_DIR}/stop-armada-server.sh and try again."
fi

echo
echo "[publish-server] Publishing Armada.Server (${ARMADA_TARGET_FRAMEWORK}) to ${PUBLISH_DIR}..."
dotnet publish "${REPO_ROOT}/src/Armada.Server" -c Release $ARMADA_DOTNET_FRAMEWORK_ARGS -o "${PUBLISH_DIR}"

# A self-rebuild slot pointer would make the start scripts keep running the old slot instead of this publish.
if [ -f "${PUBLISH_DIR}/current" ]; then
    rm -f "${PUBLISH_DIR}/current"
    echo "[publish-server] Cleared ${PUBLISH_DIR}/current, so the start scripts run this publish rather than an older slot."
fi

echo
echo "[publish-server] Deploying dashboard assets..."
# Same rule as publish-server.bat: keep going only when a previously deployed dashboard is there to serve.
if ! "${SCRIPT_DIR}/deploy-dashboard.sh"; then
    if [ -f "${HOME}/.armada/dashboard/index.html" ]; then
        echo "[publish-server] WARNING: Dashboard deploy failed. Keeping the previously deployed React dashboard."
    else
        echo "ERROR: Dashboard deploy failed and no deployed React dashboard is available." >&2
        exit 1
    fi
fi

if [ ! -f "${SERVER_EXE}" ]; then
    echo "ERROR: Published server executable not found at ${SERVER_EXE}" >&2
    exit 1
fi

echo
echo "[publish-server] Completed."
echo "[publish-server] Server executable: ${SERVER_EXE}"
