#!/usr/bin/env bash
set -euo pipefail

if [ -z "${1:-}" ]; then
    echo "Usage: build-all.sh <tag>"
    echo "Example: build-all.sh v1.0.0"
    exit 1
fi

TAG="$1"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "=== Building Armada Admiral image ==="
"${SCRIPT_DIR}/build-admiral.sh" "$TAG"

echo "=== Building Armada dashboard image ==="
"${SCRIPT_DIR}/build-dashboard.sh" "$TAG"

echo "=== Building Armada proxy image ==="
"${SCRIPT_DIR}/build-proxy.sh" "$TAG"

echo "=== All images built for ${TAG} and latest, pushed to Docker Hub, and pulled locally ==="
