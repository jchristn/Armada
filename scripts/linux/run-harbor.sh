#!/usr/bin/env bash
# Build and run Armada Harbor from source in the foreground. Linux entry point for the shared implementation in scripts/common/run-harbor.sh, which
# macOS and Linux both use; run with -h for usage.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
die() { echo "ERROR: $*" >&2; exit 1; }
[ "$(uname -s)" = Linux ] || die "This script is for Linux. Use scripts/macos on macOS or scripts\\windows on Windows."
if grep -qi microsoft /proc/version 2>/dev/null \
    && { [ -n "${WSL_DISTRO_NAME:-}" ] || [ -d /run/WSL ] || ls /proc/sys/fs/binfmt_misc/WSLInterop* >/dev/null 2>&1; }; then
    die "This is WSL. Use the same script in scripts/wsl2 (WSL 2) or scripts/wsl (WSL 1); they handle the Windows PATH, line endings and Windows startup."
fi

# Messages in the shared scripts name this directory (scripts/linux/...).
export ARMADA_SCRIPT_OS=linux
exec "${SCRIPT_DIR}/../common/run-harbor.sh" "$@"
