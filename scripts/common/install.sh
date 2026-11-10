#!/usr/bin/env bash
# Build Armada, deploy the dashboard into ~/.armada/dashboard and install the armada CLI (Armada.Helm) as a
# global .NET tool. Fails when Armada.Helm is already installed (use reinstall.sh) and while an Armada server
# is running (stop it with stop-armada-server.sh first).
#
# Usage: install.sh [<framework> | -f <framework> | --framework <framework>] [-k|--insecure]
#   <framework>  net10.0 (default, or ARMADA_TARGET_FRAMEWORK) or net8.0
#   --insecure   also -k, --no-strict-ssl, --ignore-cert-errors: skip TLS certificate checks for npm/Node only
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
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
echo "[install] Checking for a running Armada server..."
RUNNING="$("${SCRIPT_DIR}/stop-armada-server.sh" --list | tr '\n' ' ')"
if [ -n "${RUNNING// /}" ]; then
    die "An Armada server is running (PID ${RUNNING% }). Stop it with scripts/${OS_DIR}/stop-armada-server.sh and try again."
fi

echo "[install] Checking for an existing install..."
if tool_installed; then
    die "Armada.Helm is already installed. Run scripts/${OS_DIR}/reinstall.sh to replace it."
fi

echo
echo "[install] Deploying dashboard..."
"$SCRIPT_DIR/deploy-dashboard.sh"

echo
echo "[install] Building Armada solution (${ARMADA_TARGET_FRAMEWORK})..."
dotnet build "$REPO_ROOT/src/Armada.sln" $ARMADA_DOTNET_MSBUILD_FRAMEWORK_ARGS

echo
echo "[install] Packing Armada.Helm (${ARMADA_TARGET_FRAMEWORK})..."
dotnet pack "$REPO_ROOT/src/Armada.Helm" $ARMADA_DOTNET_MSBUILD_FRAMEWORK_ARGS -o "$REPO_ROOT/src/nupkg"

echo
echo "[install] Installing Armada.Helm as a global tool..."
dotnet tool install --global --add-source "$REPO_ROOT/src/nupkg" Armada.Helm

# dotnet tool install --global puts the armada command in ~/.dotnet/tools.
case ":$PATH:" in
    *":$HOME/.dotnet/tools:"*) ;;
    *)
        echo "[install] Note: $HOME/.dotnet/tools is not on your PATH, so new shells will not find 'armada'." >&2
        echo "[install] Add this line to your shell profile: export PATH=\"\$PATH:\$HOME/.dotnet/tools\"" >&2
        ;;
esac

echo
echo "[install] Completed."
