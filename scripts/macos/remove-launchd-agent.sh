#!/usr/bin/env bash
# Deprecated name, kept so existing docs, notes and habits keep working: remove-launchd-agent.sh is now remove-startup.sh, the
# start-at-login script name used on every OS (scripts/{macos,linux,wsl,wsl2}/remove-startup.sh, scripts\windows\remove-startup.bat).
# This forwards all arguments to scripts/macos/remove-startup.sh; run that with -h for usage.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
echo "[remove-launchd-agent] Note: this script is now scripts/macos/remove-startup.sh; running that." >&2
exec "${SCRIPT_DIR}/remove-startup.sh" "$@"
