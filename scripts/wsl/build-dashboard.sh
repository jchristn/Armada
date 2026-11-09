#!/usr/bin/env sh
# Builds and pushes the Armada Docker image(s) with docker buildx. WSL 1 counterpart of scripts/linux/build-dashboard.sh.
# Needs a Docker engine, which WSL 1 does not have, so this copy only explains the alternatives.
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl
# shellcheck source=_wsl-common.sh source-path=SCRIPTDIR
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
wsl_die "WSL 1 has no Docker engine. Turn on Docker Desktop's WSL 2 integration and use scripts/wsl2, or run scripts\windows\build-dashboard.bat from Windows."
