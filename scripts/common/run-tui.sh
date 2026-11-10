#!/usr/bin/env bash
# Build and run the Armada terminal UI (armada tui, hosted by the CLI in src/Armada.Helm) from source in the foreground.
#
# Usage:
#   scripts/macos/run-tui.sh or scripts/linux/run-tui.sh [-f <framework>|--framework <framework>|<framework>] [tui arguments...]
# The framework (e.g. net8.0 or net10.0) is used to both build and run; default net10.0 (or ARMADA_TARGET_FRAMEWORK).
# Any remaining arguments are passed to "armada tui", for example: scripts/macos/run-tui.sh net10.0 --profile work
# The TUI talks to the Admiral in ~/.armada/tui.json (profiles; see docs/TUI.md).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
die() { echo "ERROR: $*" >&2; exit 1; }
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }
case "${1:-}" in -h|--help) usage; exit 0 ;; esac

# shellcheck source=scripts/common/resolve-framework.sh
. "${REPO_ROOT}/scripts/common/resolve-framework.sh"
armada_resolve_framework "$@"
shift "${ARMADA_FRAMEWORK_ARGS_CONSUMED}"
command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download"
FRAMEWORK="${ARMADA_TARGET_FRAMEWORK}"

echo "[run-tui] Building the Armada CLI (${FRAMEWORK})..."
dotnet build "${REPO_ROOT}/src/Armada.Helm/Armada.Helm.csproj" ${ARMADA_DOTNET_FRAMEWORK_ARGS}

echo "[run-tui] Starting armada tui (${FRAMEWORK})..."
exec dotnet run --project "${REPO_ROOT}/src/Armada.Helm" --framework "${FRAMEWORK}" --no-build -- tui "$@"
