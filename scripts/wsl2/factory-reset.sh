#!/usr/bin/env sh
# Wipes Armada back to a factory-fresh state (database and runtime state under ~/.armada). WSL 2 counterpart of scripts/linux/factory-reset.sh.
# Works with either start-at-login mode: it stops the systemd unit when systemd runs, then any Armada.Server by PID (including one started by the Windows Startup launcher). The closing hints name the scripts/linux paths; use the scripts/wsl2 scripts of the same name.
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl2
# shellcheck source=_wsl-common.sh source-path=SCRIPTDIR
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
wsl_run "$SCRIPT_DIR/../linux/factory-reset.sh" "$@"
