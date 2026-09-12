#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TAG="$1"

echo "=== Building Armada server ==="
"${SCRIPT_DIR}/build-server.sh" "$TAG" || exit 1

echo "=== Building Armada dashboard ==="
"${SCRIPT_DIR}/build-dashboard.sh" "$TAG" || exit 1

echo "=== All Armada images built, pushed to Docker Hub, and pulled into the local registry ==="
