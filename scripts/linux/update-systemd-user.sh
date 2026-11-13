#!/usr/bin/env bash
# Deprecated name, kept so existing docs, notes and habits keep working: update-systemd-user.sh is now update-startup.sh, the
# start-at-login script name used on every OS (scripts/{macos,linux,wsl,wsl2}/update-startup.sh, scripts\windows\update-startup.bat).
# This forwards all arguments to scripts/linux/update-startup.sh; run that with -h for usage.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
echo "[update-systemd-user] Note: this script is now scripts/linux/update-startup.sh; running that." >&2
exec "${SCRIPT_DIR}/update-startup.sh" "$@"
