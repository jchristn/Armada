#!/usr/bin/env sh
# Publishes Armada.Server to ~/.armada/bin and starts it at login. WSL 2 counterpart of
# scripts/linux/install-systemd-user.sh, with the same name so the docs map one-to-one.
#
# Start-at-login works in one of two ways:
#   - systemd is running (WSL 2 with [boot] systemd=true in /etc/wsl.conf; wsl_has_systemd): the Linux
#     script runs unchanged and installs ~/.config/systemd/user/armada.service. systemd only runs while
#     the distro is up, so Armada starts when the distro starts, not at Windows login.
#   - no systemd (WSL 1 never has it; WSL 2 without the setting): nothing in WSL reads systemd units or
#     desktop autostart entries, so a hidden launcher (armada-wsl.vbs) goes in the Windows Startup folder.
#     At Windows login it runs ~/.armada/bin/Armada.Server through wsl.exe and keeps the distro running.
#     There is no Restart=on-failure in this mode; rerun this script or log in again after a crash.
# Arguments (framework, --insecure) are forwarded to publish-server.sh, as on Linux.
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

ARMADA_HOME="$HOME/.armada"
SERVER_EXE="$ARMADA_HOME/bin/Armada.Server"

if wsl_has_systemd; then
    # Two registered Admirals would race for the same ports, so drop a launcher left by the other mode.
    if (wsl_startup_remove armada) 2>/dev/null; then
        echo "[install-systemd-user] Removed the earlier Windows Startup launcher; systemd runs Armada now."
    fi
    wsl_run "$SCRIPT_DIR/../linux/install-systemd-user.sh" "$@"
fi

echo
echo "[install-systemd-user] systemd is not running in this distro; using the Windows Startup folder instead."
echo "[install-systemd-user] Stopping any running Armada.Server..."
bash "$REPO_ROOT/scripts/common/stop-armada-server.sh"

echo
echo "[install-systemd-user] Publishing Armada.Server..."
bash "$SCRIPT_DIR/../linux/publish-server.sh" "$@"

mkdir -p "$ARMADA_HOME/logs"
wsl_startup_install armada env -C "$ARMADA_HOME" "$SERVER_EXE"

echo
echo "[install-systemd-user] Starting Armada.Server now..."
nohup env -C "$ARMADA_HOME" "$SERVER_EXE" >"$ARMADA_HOME/logs/wsl-startup.log" 2>&1 &

bash "$SCRIPT_DIR/../linux/healthcheck-server.sh" "${ARMADA_BASE_URL:-http://localhost:7890}"

echo
echo "[install-systemd-user] Completed."
echo "[install-systemd-user] Armada starts at Windows login through the Startup folder launcher."
echo "[install-systemd-user] The server started now keeps running while this distro is up (an open WSL"
echo "[install-systemd-user] terminal, or the launcher after the next login). Output: $ARMADA_HOME/logs/wsl-startup.log"
