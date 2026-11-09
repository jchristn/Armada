#!/usr/bin/env sh
# Builds and runs the Armada server in the background and Harbor in the foreground, for development. WSL 1 counterpart of scripts/linux/run-local.sh.
# Harbor is a GUI app; WSL 1 has no display, so this needs an X server on Windows (DISPLAY set).
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl
# shellcheck source=_wsl-common.sh source-path=SCRIPTDIR
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
wsl_require_cmd dotnet "Install the .NET SDK inside WSL (https://learn.microsoft.com/dotnet/core/install/linux)."
if [ -z "${DISPLAY:-}" ] && [ -z "${WAYLAND_DISPLAY:-}" ]; then
    wsl_die "Armada Harbor is a desktop (Avalonia) app and WSL 1 has no display. Run scripts\\windows\\run-local.bat from Windows, use scripts/wsl2 with WSLg, or start an X server on Windows and set DISPLAY."
fi
wsl_run "$SCRIPT_DIR/../linux/run-local.sh" "$@"
