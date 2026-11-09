#!/usr/bin/env sh
# Builds the React dashboard and deploys it to ~/.armada/dashboard. WSL 2 counterpart of scripts/linux/deploy-dashboard.sh.
# Windows node/npm on the PATH are ignored, so the dashboard builds with Linux Node (or falls back to the pre-built dist). If a node_modules installed from Windows breaks the build, it is reinstalled from the lockfile.
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl2
# shellcheck source=_wsl-common.sh source-path=SCRIPTDIR
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
wsl_run "$SCRIPT_DIR/../linux/deploy-dashboard.sh" "$@"
