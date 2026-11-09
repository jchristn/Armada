#!/usr/bin/env sh
# Uninstalls and reinstalls the armada global .NET tool. WSL 1 counterpart of scripts/linux/reinstall.sh.
# Windows dotnet on the PATH is ignored and ~/.dotnet/tools is added to it.
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
wsl_run "$SCRIPT_DIR/../linux/reinstall.sh" "$@"
