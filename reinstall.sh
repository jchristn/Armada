#!/usr/bin/env bash
set -euo pipefail

echo
echo "[reinstall] Removing existing global Armada.Helm tool if present..."
dotnet tool uninstall --global Armada.Helm >/dev/null 2>&1 || true

echo
echo "[reinstall] Running fresh install..."
"$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/install.sh"
