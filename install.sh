#!/usr/bin/env bash
set -euo pipefail

echo
echo "[install] Deploying dashboard..."
"$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/deploy-dashboard.sh"

echo
echo "[install] Building Armada solution..."
dotnet build src/Armada.sln

echo
echo "[install] Packing Armada.Helm..."
dotnet pack src/Armada.Helm -o ./src/nupkg

echo
echo "[install] Installing Armada.Helm as a global tool..."
dotnet tool install --global --add-source ./src/nupkg Armada.Helm

echo
echo "[install] Completed."
