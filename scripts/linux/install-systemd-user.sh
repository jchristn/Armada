#!/usr/bin/env bash
# Deprecated name, kept so existing docs, notes and habits keep working: install-systemd-user.sh is now install-startup.sh,
# the start-at-login script name used on every OS (scripts/{macos,linux,wsl,wsl2}/install-startup.sh,
# scripts\windows\install-startup.bat). The old script could be run again to republish and restart, so this
# forwards to scripts/linux/update-startup.sh when Armada is already registered and to install-startup.sh otherwise,
# with all arguments; run those with -h for usage.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UNIT_PATH="${XDG_CONFIG_HOME:-${HOME}/.config}/systemd/user/armada.service"
if { [ -f "$UNIT_PATH" ] && ! grep -q -- '--run-service' "$UNIT_PATH"; } || [ -f "${XDG_CONFIG_HOME:-${HOME}/.config}/autostart/armada.desktop" ]; then
    echo "[install-systemd-user] Note: this script is now scripts/linux/install-startup.sh; Armada is registered, so running update-startup.sh." >&2
    exec "${SCRIPT_DIR}/update-startup.sh" "$@"
fi
echo "[install-systemd-user] Note: this script is now scripts/linux/install-startup.sh; running that." >&2
exec "${SCRIPT_DIR}/install-startup.sh" "$@"
