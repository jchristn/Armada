#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo
echo "[install-mcp] Configuring Armada MCP for Claude Code, Codex, Gemini, and Cursor..."
dotnet run --project "$SCRIPT_DIR/src/Armada.Helm" -f net8.0 -- mcp install --yes

echo
echo "[install-mcp] Completed."
