#!/usr/bin/env bash
# Deprecated name, kept so existing docs, notes and habits keep working: update-launchd-agent.sh is now update-startup.sh, the
# start-at-login script name used on every OS (scripts/{macos,linux,wsl,wsl2}/update-startup.sh, scripts\windows\update-startup.bat).
# This forwards all arguments to scripts/macos/update-startup.sh; run that with -h for usage.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
echo "[update-launchd-agent] Note: this script is now scripts/macos/update-startup.sh; running that." >&2
exec "${SCRIPT_DIR}/update-startup.sh" "$@"
