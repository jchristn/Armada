#!/usr/bin/env bash
# Build and run Armada Harbor (the host-side runner, src/Armada.Harbor) from source in the foreground.
#
# Usage:
#   scripts/macos/run-harbor.sh or scripts/linux/run-harbor.sh [-f <framework>|--framework <framework>|<framework>] [harbor arguments...]
# The framework (e.g. net8.0 or net10.0) is used to both build and run; default net10.0 (or ARMADA_TARGET_FRAMEWORK).
# Any remaining arguments are passed to Harbor, for example: scripts/macos/run-harbor.sh net10.0 --install-startup
# Harbor's server link URL, access key, and capabilities are set in its Settings window (see docs/HARBOR.md).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

# shellcheck source=scripts/common/resolve-framework.sh
. "${REPO_ROOT}/scripts/common/resolve-framework.sh"
armada_resolve_framework "$@"
shift "${ARMADA_FRAMEWORK_ARGS_CONSUMED}"
FRAMEWORK="${ARMADA_TARGET_FRAMEWORK}"

echo "[run-harbor] Building Armada Harbor (${FRAMEWORK})..."
dotnet build "${REPO_ROOT}/src/Armada.Harbor/Armada.Harbor.csproj" --framework "${FRAMEWORK}"

echo "[run-harbor] Starting Armada Harbor (${FRAMEWORK})..."
exec dotnet run --project "${REPO_ROOT}/src/Armada.Harbor" --framework "${FRAMEWORK}" --no-build -- "$@"
