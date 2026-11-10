#!/usr/bin/env bash
# Build and run the Armada terminal UI (armada tui, hosted by the CLI in src/Armada.Helm) from source in the
# foreground. Any arguments after the framework are passed to "armada tui". The TUI talks to the Admiral in
# ~/.armada/tui.json (profiles; see docs/TUI.md), or ARMADA_SERVER_URL.
# WSL 1 version: WSL 1 shares the Windows network, so localhost also reaches an Admiral running on Windows.
#
# Usage: scripts/wsl/run-tui.sh [<framework> | -f <framework> | --framework <framework>] [tui arguments...]
#   <framework>  net10.0 (default, or ARMADA_TARGET_FRAMEWORK) or net8.0, used to both build and run
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "ERROR: $*" >&2; exit 1; }
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }

# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl
# shellcheck source=_wsl-common.sh
. "$SCRIPT_DIR/_wsl-common.sh"
# The helper's own errors get the same "ERROR:" prefix as every other Armada script.
wsl_die() { die "$@"; }
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
case "${1:-}" in -h|--help) usage; exit 0 ;; esac

# shellcheck source=../common/resolve-framework.sh
. "$REPO_ROOT/scripts/common/resolve-framework.sh"
armada_resolve_framework "$@"
shift "$ARMADA_FRAMEWORK_ARGS_CONSUMED"
FRAMEWORK="$ARMADA_TARGET_FRAMEWORK"
wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"

echo "[run-tui] Building the Armada CLI (${FRAMEWORK})..."
# shellcheck disable=SC2086 # the framework arguments are several words
dotnet build "$REPO_ROOT/src/Armada.Helm/Armada.Helm.csproj" $ARMADA_DOTNET_FRAMEWORK_ARGS

echo "[run-tui] Starting armada tui (${FRAMEWORK})..."
exec dotnet run --project "$REPO_ROOT/src/Armada.Helm" --framework "$FRAMEWORK" --no-build -- tui "$@"
