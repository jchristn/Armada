#!/usr/bin/env bash
set -euo pipefail

# Builds, tests and packages the Armada VS Code extension (src/Armada.VSCode) into
# vsix/armada-vscode-<version>.vsix (version from its package.json). With --install, also installs it into VS Code.

usage() {
    echo "Usage: build-vscode.sh [--install]"
    echo "Builds vsix/armada-vscode-<version>.vsix; --install also installs it into VS Code."
}

INSTALL=0
case "${1:-}" in
    "") ;;
    --install) INSTALL=1 ;;
    *) usage; exit 1 ;;
esac

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "${REPO_ROOT}/src/Armada.VSCode"

echo "Installing extension dependencies..."
npm ci

echo "Installing dashboard dependencies (the page tabs bundle the dashboard's pages)..."
(cd "${REPO_ROOT}/src/Armada.Dashboard" && npm ci)

echo "Building the extension..."
npm run build

echo "Linting the extension and the dashboard modules it shares..."
npm run lint

echo "Running the extension unit tests..."
npm test

echo "Packaging the VSIX..."
npm run package

VERSION="$(node -p "require('./package.json').version")"
VSIX="${REPO_ROOT}/vsix/armada-vscode-${VERSION}.vsix"
echo "Done. VSIX: ${VSIX}"

if [ "${INSTALL}" = "1" ]; then
    CODE_CLI="$(command -v code || true)"
    MAC_CODE_CLI="/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code"
    if [ -z "${CODE_CLI}" ] && [ -x "${MAC_CODE_CLI}" ]; then
        CODE_CLI="${MAC_CODE_CLI}"
    fi
    if [ -z "${CODE_CLI}" ]; then
        echo "The VS Code 'code' command was not found; install ${VSIX} from the Extensions view instead." >&2
        exit 1
    fi
    "${CODE_CLI}" --install-extension "${VSIX}" --force
    echo "Installed. Reload any open VS Code windows (Developer: Reload Window)."
fi
