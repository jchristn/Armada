#!/usr/bin/env bash
# Build and run Armada Harbor (the host-side runner, src/Armada.Harbor) from source in the foreground.
# Any arguments after the framework are passed to Harbor. Harbor's server link URL, access key and capabilities are
# set in its Settings window (see docs/HARBOR.md).
# WSL 1 version: WSL 1 has no WSLg, so this needs an X server running on Windows with DISPLAY set; there is no tray.
# Harbor's own --install-startup writes a Linux login item, which does not run at Windows login.
#
# Usage: scripts/wsl/run-harbor.sh [<framework> | -f <framework> | --framework <framework>] [harbor arguments...]
#   <framework>  net10.0 (default, or ARMADA_TARGET_FRAMEWORK) or net8.0, used to both build and run
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

# shellcheck source=../common/resolve-framework.sh
. "$REPO_ROOT/scripts/common/resolve-framework.sh"
armada_resolve_framework "$@"
shift "$ARMADA_FRAMEWORK_ARGS_CONSUMED"
FRAMEWORK="$ARMADA_TARGET_FRAMEWORK"
wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
# Harbor is a desktop (Avalonia) app. WSL 1 has no WSLg, so it needs an X server on Windows and DISPLAY set.
if [ -z "${DISPLAY:-}" ]; then
    die "WSL 1 has no WSLg, so it cannot show Armada Harbor. Run scripts\\windows\\run-harbor.bat from Windows, move this distro to WSL 2 (wsl --set-version ${WSL_DISTRO_NAME:-<distro>} 2) and use scripts/wsl2, or start an X server on Windows and set DISPLAY."
fi
# A display alone is not enough: the distro needs the X11 client libraries Avalonia loads (stock WSL images lack them).
wsl_check_libs error "Armada Harbor" "$WSL_AVALONIA_LIBS"
for arg in "$@"; do
    if [ "$arg" = --install-startup ]; then
        echo "[run-harbor] Note: Harbor's --install-startup writes a Linux login item, which does not run at Windows login." >&2
    fi
done

echo "[run-harbor] Building Armada Harbor (${FRAMEWORK})..."
# shellcheck disable=SC2086 # the framework arguments are several words
dotnet build "$REPO_ROOT/src/Armada.Harbor/Armada.Harbor.csproj" $ARMADA_DOTNET_FRAMEWORK_ARGS

echo "[run-harbor] Starting Armada Harbor (${FRAMEWORK})..."
# Give Harbor the Windows PATH entries back (explorer.exe, wslview, the Windows browser) and BROWSER=wslview.
wsl_prepare_app_env
exec dotnet run --project "$REPO_ROOT/src/Armada.Harbor" --framework "$FRAMEWORK" --no-build -- "$@"
