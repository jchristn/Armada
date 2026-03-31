#!/usr/bin/env bash
set -euo pipefail

echo
echo "[remove] Uninstalling global Armada.Helm tool if present..."
dotnet tool uninstall --global Armada.Helm >/dev/null 2>&1 || true

echo
echo "[remove] Removing deployed dashboard from ${HOME}/.armada/dashboard"
rm -rf "${HOME}/.armada/dashboard"

echo
echo "[remove] Completed."
