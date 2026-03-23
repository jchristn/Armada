#!/usr/bin/env bash
set -e

echo
echo "[update] Stopping Armada server if it is running..."
armada server stop || true

echo
echo "[update] Reinstalling Armada tool and redeploying dashboard..."
"$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/reinstall.sh"

echo
echo "[update] Starting Armada server..."
armada server start
