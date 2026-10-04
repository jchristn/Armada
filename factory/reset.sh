#!/usr/bin/env bash
# ====================================================================
# Armada factory reset (Linux/macOS)
#
# Stops Armada processes, wipes ~/.armada runtime state (db, logs,
# docks, repos, settings), and copies the gold settings.json from
# this directory back into place. Requires typing RESET to proceed.
# ====================================================================
set -u

SCRIPT_DIR="$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
TARGET_DIR="${HOME}/.armada"
GOLD_SETTINGS="${SCRIPT_DIR}/settings.json"

echo
echo "========================================================"
echo "  ARMADA FACTORY RESET"
echo "========================================================"
echo
echo "This will:"
echo "  1. Stop any running Armada processes (Armada.Server, Armada.Helm)."
echo "  2. Delete the database, logs, docks, repos, and settings in:"
echo "       ${TARGET_DIR}"
echo "  3. Replace settings.json with the factory gold copy from:"
echo "       ${GOLD_SETTINGS}"
echo
echo "This action cannot be undone. All missions, voyages, captains,"
echo "vessels, and local worktrees tracked by Armada will be removed."
echo
printf "Type RESET (in capitals) to proceed: "
read -r CONFIRM
if [ "${CONFIRM}" != "RESET" ]; then
    echo "Aborted. No changes were made."
    exit 1
fi

echo
echo "[1/3] Stopping Armada processes..."
# pkill returns non-zero when no match is found, which is fine here.
pkill -f "Armada.Server"  > /dev/null 2>&1 || true
pkill -f "Armada.Helm"    > /dev/null 2>&1 || true
# Let the OS release SQLite file locks before we delete.
sleep 1

echo "[2/3] Removing runtime data from ${TARGET_DIR} ..."
rm -rf \
    "${TARGET_DIR}/logs" \
    "${TARGET_DIR}/docks" \
    "${TARGET_DIR}/repos"
rm -f \
    "${TARGET_DIR}/armada.db" \
    "${TARGET_DIR}/armada.db-wal" \
    "${TARGET_DIR}/armada.db-shm" \
    "${TARGET_DIR}/settings.json"

echo "[3/3] Restoring gold settings.json ..."
if [ ! -f "${GOLD_SETTINGS}" ]; then
    echo "ERROR: factory gold file not found at ${GOLD_SETTINGS}" >&2
    exit 2
fi
mkdir -p "${TARGET_DIR}"
cp "${GOLD_SETTINGS}" "${TARGET_DIR}/settings.json"

echo
echo "========================================================"
echo "  Reset complete."
echo "========================================================"
echo
echo "Restart Armada to rebuild the database from defaults:"
echo
echo "  cd .."
echo "  armada server start"
echo
