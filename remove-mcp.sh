#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo
echo "[remove-mcp] Removing Armada MCP for Claude Code, Codex, Gemini, and Cursor..."
dotnet run --project "$SCRIPT_DIR/src/Armada.Helm" -f net8.0 -- mcp remove --yes

echo
echo "[remove-mcp] Completed."
