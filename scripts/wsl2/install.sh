#!/usr/bin/env sh
# Builds Armada, deploys the dashboard and installs the armada CLI as a global .NET tool. WSL 2 counterpart of scripts/linux/install.sh.
# Windows dotnet/node on the PATH are ignored and ~/.dotnet/tools is added to it. Windows node/npm on the PATH are ignored, so the dashboard builds with Linux Node (or falls back to the pre-built dist).
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
wsl_run "$SCRIPT_DIR/../linux/install.sh" "$@"
