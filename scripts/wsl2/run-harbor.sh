#!/usr/bin/env sh
# Builds and runs Armada Harbor from source in the foreground. WSL 2 counterpart of scripts/linux/run-harbor.sh.
# Harbor is a GUI app and needs WSLg for its window and tray icon.
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl2
# shellcheck source=_wsl-common.sh source-path=SCRIPTDIR
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
wsl_require_cmd dotnet "Install the .NET SDK inside WSL (https://learn.microsoft.com/dotnet/core/install/linux)."
if [ -z "${DISPLAY:-}" ] && [ -z "${WAYLAND_DISPLAY:-}" ]; then
    wsl_die "Armada Harbor is a desktop (Avalonia) app and needs WSLg (Windows 11, or Windows 10 with the Store version of WSL). No DISPLAY or WAYLAND_DISPLAY is set; run 'wsl --update', or use scripts\\windows\\run-harbor.bat from Windows."
fi
wsl_run "$SCRIPT_DIR/../linux/run-harbor.sh" "$@"
