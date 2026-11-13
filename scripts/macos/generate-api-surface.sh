#!/usr/bin/env bash
# Regenerate docs/api-surface-1.0.json and docs/API_SURFACE_1.0.md. macOS entry point for the shared implementation in scripts/common/generate-api-surface.sh, which
# macOS and Linux both use; run with -h for usage.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
die() { echo "ERROR: $*" >&2; exit 1; }
[ "$(uname -s)" = Darwin ] || die "This script is for macOS. Use scripts/linux on Linux, scripts/wsl2 or scripts/wsl on WSL, or scripts\\windows on Windows."

# Messages in the shared scripts name this directory (scripts/macos/...).
export ARMADA_SCRIPT_OS=macos
exec "${SCRIPT_DIR}/../common/generate-api-surface.sh" "$@"
