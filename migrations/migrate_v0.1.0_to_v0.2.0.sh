#!/usr/bin/env bash
set -euo pipefail

echo ""
echo "Armada Settings Migration: v0.1.0 to v0.2.0"
echo "============================================="
echo ""

# Check for jq
if ! command -v jq &> /dev/null; then
    echo "ERROR: jq is required but not installed."
    echo ""
    echo "Install jq using your package manager:"
    echo "  Ubuntu/Debian: sudo apt install jq"
    echo "  macOS:         brew install jq"
    echo "  Fedora/RHEL:   sudo dnf install jq"
    echo "  Arch:          sudo pacman -S jq"
    exit 1
fi

# Determine settings file path
SETTINGS_FILE="${1:-$HOME/.armada/settings.json}"

echo "Settings file: $SETTINGS_FILE"

# Check if the file exists
if [ ! -f "$SETTINGS_FILE" ]; then
    echo ""
    echo "ERROR: Settings file not found: $SETTINGS_FILE"
    echo ""
    echo "Usage: $0 [path/to/settings.json]"
    echo "Default path: ~/.armada/settings.json"
    exit 1
fi

# Validate JSON
if ! jq empty "$SETTINGS_FILE" 2>/dev/null; then
    echo ""
    echo "ERROR: Failed to parse settings.json. The file may contain invalid JSON."
    exit 1
fi

# Check if databasePath exists
if ! jq -e '.databasePath' "$SETTINGS_FILE" > /dev/null 2>&1; then
    echo ""
    echo "WARNING: No databasePath property found in settings.json."
    echo "The file may have already been migrated to v0.2.0 format."
    exit 0
fi

# Extract the current databasePath value
DB_PATH=$(jq -r '.databasePath' "$SETTINGS_FILE")

# Create backup
BACKUP_PATH="${SETTINGS_FILE}.v0.1.0.bak"
cp "$SETTINGS_FILE" "$BACKUP_PATH"
echo "Backup created: $BACKUP_PATH"

# Perform the migration: remove databasePath, add database object
jq --arg dbpath "$DB_PATH" '
    del(.databasePath) |
    .database = {
        "type": "Sqlite",
        "filename": $dbpath,
        "hostname": "localhost",
        "port": 0,
        "username": "",
        "password": "",
        "databaseName": "",
        "schema": "",
        "requireEncryption": false,
        "logQueries": false,
        "minPoolSize": 1,
        "maxPoolSize": 25,
        "connectionLifetimeSeconds": 300,
        "connectionIdleTimeoutSeconds": 60
    }
' "$SETTINGS_FILE" > "${SETTINGS_FILE}.tmp" && mv "${SETTINGS_FILE}.tmp" "$SETTINGS_FILE"

echo ""
echo "Migration complete!"
echo "  Original (backed up): $BACKUP_PATH"
echo "  Updated: $SETTINGS_FILE"
echo ""
echo "Changes:"
echo "  - Removed 'databasePath': $DB_PATH"
echo "  - Added 'database' object with connection pooling settings"
