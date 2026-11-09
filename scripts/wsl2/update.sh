#!/usr/bin/env sh
# Stops Armada, reinstalls the CLI and dashboard from this checkout, then restarts the server. WSL 2 counterpart of scripts/linux/update.sh.
# Windows dotnet on the PATH is ignored and ~/.dotnet/tools is added to it, so the Linux armada tool is used.
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
wsl_run "$SCRIPT_DIR/../linux/update.sh" "$@"
