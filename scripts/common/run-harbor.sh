#!/usr/bin/env bash
# Build and run Armada Harbor (the host-side runner, src/Armada.Harbor) from source in the foreground.
#
# Usage:
#   scripts/macos/run-harbor.sh or scripts/linux/run-harbor.sh [-f <framework>|--framework <framework>|<framework>] [harbor arguments...]
# The framework (e.g. net8.0 or net10.0) is used to both build and run; default net10.0 (or ARMADA_TARGET_FRAMEWORK).
# Any remaining arguments are passed to Harbor, for example: scripts/macos/run-harbor.sh net10.0 --install-startup
# Harbor's server link URL, access key, and capabilities are set in its Settings window (see docs/HARBOR.md).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
die() { echo "ERROR: $*" >&2; exit 1; }
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }
case "${1:-}" in -h|--help) usage; exit 0 ;; esac

# shellcheck source=scripts/common/resolve-framework.sh
. "${REPO_ROOT}/scripts/common/resolve-framework.sh"
armada_resolve_framework "$@"
shift "${ARMADA_FRAMEWORK_ARGS_CONSUMED}"
command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download"
# Native-library checks (ICU, Avalonia, clipboard); they do nothing where there is no ldconfig (macOS).
# shellcheck source=scripts/common/native-libs.sh
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/native-libs.sh"
native_check_icu
# Harbor is a desktop (Avalonia) app: on Linux it needs a graphical session.
if [ "$(uname -s)" = Linux ] && [ -z "${DISPLAY:-}" ] && [ -z "${WAYLAND_DISPLAY:-}" ]; then
    die "Armada Harbor is a desktop app and needs a graphical session, but neither DISPLAY nor WAYLAND_DISPLAY is set."
fi
if [ "$(uname -s)" = Linux ]; then
    native_check_libs error "Armada Harbor" "$NATIVE_AVALONIA_LIBS"
fi
FRAMEWORK="${ARMADA_TARGET_FRAMEWORK}"

echo "[run-harbor] Building Armada Harbor (${FRAMEWORK})..."
dotnet build "${REPO_ROOT}/src/Armada.Harbor/Armada.Harbor.csproj" ${ARMADA_DOTNET_FRAMEWORK_ARGS}

echo "[run-harbor] Starting Armada Harbor (${FRAMEWORK})..."
exec dotnet run --project "${REPO_ROOT}/src/Armada.Harbor" --framework "${FRAMEWORK}" --no-build -- "$@"
