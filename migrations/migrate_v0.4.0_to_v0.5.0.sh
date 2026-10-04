#!/usr/bin/env bash
set -euo pipefail

BACKEND="${1:-all}"

print_sqlite() {
cat <<'SQL'
-- SQLite
ALTER TABLE captains ADD COLUMN model TEXT NULL;
ALTER TABLE missions ADD COLUMN total_runtime_ms BIGINT NULL;
INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc) VALUES (26, 'Add model to captains', datetime('now'));
INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc) VALUES (27, 'Add total_runtime_ms to missions', datetime('now'));
SQL
}

print_postgresql() {
cat <<'SQL'
-- PostgreSQL
ALTER TABLE captains ADD COLUMN model TEXT NULL;
ALTER TABLE missions ADD COLUMN total_runtime_ms BIGINT NULL;
INSERT INTO schema_migrations (version, description, applied_utc) VALUES (26, 'Add model to captains', NOW()) ON CONFLICT (version) DO NOTHING;
INSERT INTO schema_migrations (version, description, applied_utc) VALUES (27, 'Add total_runtime_ms to missions', NOW()) ON CONFLICT (version) DO NOTHING;
SQL
}

print_sqlserver() {
cat <<'SQL'
-- SQL Server
ALTER TABLE captains ADD model NVARCHAR(MAX) NULL;
ALTER TABLE missions ADD total_runtime_ms BIGINT NULL;
IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE version = 26)
    INSERT INTO schema_migrations (version, description, applied_utc) VALUES (26, 'Add model to captains', SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE version = 27)
    INSERT INTO schema_migrations (version, description, applied_utc) VALUES (27, 'Add total_runtime_ms to missions', SYSUTCDATETIME());
SQL
}

print_mysql() {
cat <<'SQL'
-- MySQL
ALTER TABLE captains ADD COLUMN model TEXT NULL;
ALTER TABLE missions ADD COLUMN total_runtime_ms BIGINT NULL;
INSERT IGNORE INTO schema_migrations (version, description, applied_utc) VALUES (26, 'Add model to captains', UTC_TIMESTAMP(6));
INSERT IGNORE INTO schema_migrations (version, description, applied_utc) VALUES (27, 'Add total_runtime_ms to missions', UTC_TIMESTAMP(6));
SQL
}

case "$BACKEND" in
    sqlite)
        print_sqlite
        ;;
    postgresql|postgres|pgsql)
        print_postgresql
        ;;
    sqlserver|mssql)
        print_sqlserver
        ;;
    mysql)
        print_mysql
        ;;
    all)
        print_sqlite
        echo
        print_postgresql
        echo
        print_sqlserver
        echo
        print_mysql
        ;;
    *)
        echo "Usage: $0 [sqlite|postgresql|sqlserver|mysql|all]" >&2
        exit 1
        ;;
esac
