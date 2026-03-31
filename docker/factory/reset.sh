#!/bin/bash
set -e

echo "========================================"
echo " Armada Factory Reset"
echo "========================================"
echo ""
echo "This will delete all database and log files."
echo "Configuration (armada.json) will be preserved."
echo ""
read -p "Type 'RESET' to confirm: " confirm
if [ "$confirm" != "RESET" ]; then
    echo "Cancelled."
    exit 1
fi
echo ""

echo "[1/3] Stopping containers..."
docker compose down
echo ""

echo "[2/3] Deleting database files..."
if [ -d "../armada/db" ]; then
    rm -f ../armada/db/*
    echo "  Database files deleted."
else
    echo "  No database directory found."
fi
echo ""

echo "[3/3] Deleting log files..."
if [ -d "../armada/logs" ]; then
    rm -f ../armada/logs/*
    echo "  Log files deleted."
else
    echo "  No logs directory found."
fi
echo ""

echo "========================================"
echo " Factory reset complete."
echo " Run 'docker compose up -d' to restart."
echo "========================================"
