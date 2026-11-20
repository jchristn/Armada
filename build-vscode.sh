#!/usr/bin/env bash
set -euo pipefail

# Builds, tests and packages the Armada VS Code extension (src/Armada.VSCode) into a .vsix.
# The VSIX version comes from src/Armada.VSCode/package.json.

if [ -n "${1:-}" ]; then
    echo "Usage: build-vscode.sh"
    echo "Builds src/Armada.VSCode/armada-<version>.vsix (version from its package.json)."
    exit 1
fi

cd "$(dirname "${BASH_SOURCE[0]}")/src/Armada.VSCode"

echo "Installing extension dependencies..."
npm ci

echo "Building the extension..."
npm run build

echo "Linting the extension and the dashboard modules it shares..."
npm run lint

echo "Running the extension unit tests..."
npm test

echo "Packaging the VSIX..."
npm run package

echo "Done. VSIX: $(ls -1 "$(pwd)"/armada-*.vsix | tail -n 1)"
