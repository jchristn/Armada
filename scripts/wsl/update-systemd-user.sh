#!/usr/bin/env sh
# Republishes Armada.Server from this checkout and restarts it. WSL 1 counterpart of
# scripts/linux/update-systemd-user.sh, with the same name so the docs map one-to-one.
#
# With systemd running in the distro (wsl_has_systemd) the Linux script runs unchanged and needs the
# armada.service unit. Without systemd (always on WSL 1) it needs the Windows Startup launcher that
# install-systemd-user.sh registers, then reruns that installer, which stops the server, republishes it
# and starts it again.
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl
# shellcheck source=_wsl-common.sh source-path=SCRIPTDIR
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"

if wsl_has_systemd; then
    wsl_run "$SCRIPT_DIR/../linux/update-systemd-user.sh" "$@"
fi

LAUNCHER="$(wsl_startup_dir)/armada-wsl.vbs"
if [ ! -f "$LAUNCHER" ]; then
    wsl_die "$LAUNCHER was not found. Run install-systemd-user.sh first."
fi

wsl_run "$SCRIPT_DIR/install-systemd-user.sh" "$@"
