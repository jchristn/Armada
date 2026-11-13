#!/usr/bin/env bash
# Deprecated name, kept so existing docs, notes and habits keep working: install-launchd-agent.sh is now install-startup.sh,
# the start-at-login script name used on every OS (scripts/{macos,linux,wsl,wsl2}/install-startup.sh,
# scripts\windows\install-startup.bat). The old script could be run again to republish and restart, so this
# forwards to scripts/macos/update-startup.sh when Armada is already registered and to install-startup.sh otherwise,
# with all arguments; run those with -h for usage.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ -f "${HOME}/Library/LaunchAgents/com.armada.admiral.plist" ]; then
    echo "[install-launchd-agent] Note: this script is now scripts/macos/install-startup.sh; Armada is registered, so running update-startup.sh." >&2
    exec "${SCRIPT_DIR}/update-startup.sh" "$@"
fi
echo "[install-launchd-agent] Note: this script is now scripts/macos/install-startup.sh; running that." >&2
exec "${SCRIPT_DIR}/install-startup.sh" "$@"
