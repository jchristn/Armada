#!/usr/bin/env sh
# Removes Armada's start-at-login registration. WSL 2 counterpart of scripts/linux/remove-systemd-user.sh,
# with the same name so the docs map one-to-one.
#
# install-systemd-user.sh registers either a systemd user unit (when systemd runs in the distro) or a
# hidden launcher in the Windows Startup folder (when it does not; always on WSL 1). This removes
# whichever exists: it disables the unit through the Linux script when systemd runs, deletes a leftover
# unit file when it does not, removes the Startup launcher, and stops the server. Published files in
# ~/.armada stay, as on Linux.
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl2
# shellcheck source=_wsl-common.sh source-path=SCRIPTDIR
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"

UNIT_PATH="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user/armada.service"

if wsl_has_systemd; then
    bash "$SCRIPT_DIR/../linux/remove-systemd-user.sh"
elif [ -f "$UNIT_PATH" ]; then
    rm -f "$UNIT_PATH"
    echo "[remove-systemd-user] Removed the unused systemd unit file $UNIT_PATH."
fi

echo
echo "[remove-systemd-user] Removing the Windows Startup launcher..."
wsl_startup_remove armada || echo "[remove-systemd-user] No Windows Startup launcher was registered."

echo
echo "[remove-systemd-user] Stopping any running Armada.Server..."
bash "$REPO_ROOT/scripts/common/stop-armada-server.sh"

echo
echo "[remove-systemd-user] Completed."
