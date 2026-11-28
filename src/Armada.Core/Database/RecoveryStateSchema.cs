namespace Armada.Core.Database
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Migration 134 (RELIABILITY_SIMPLICITY_PLAN.md RS-3.7): the recovery_state table, which keeps endpoint breakers,
    /// launch backoff, transient outage starts, and escalation cooldowns across an Admiral restart.
    /// </summary>
    internal static class RecoveryStateSchema
    {
        internal const int Version = 134;

        internal static SchemaMigration Migration(DatabaseTypeEnum provider)
        {
            const string description = "Add recovery_state for recovery state that survives a restart";
            switch (provider)
            {
                case DatabaseTypeEnum.Sqlite:
                    return new SchemaMigration(Version, description,
                        "CREATE TABLE IF NOT EXISTS recovery_state (kind TEXT NOT NULL, state_key TEXT NOT NULL, payload_json TEXT NOT NULL, expires_utc TEXT NOT NULL, last_update_utc TEXT NOT NULL, PRIMARY KEY (kind, state_key));",
                        "CREATE INDEX IF NOT EXISTS idx_recovery_state_expires ON recovery_state(expires_utc);");
                case DatabaseTypeEnum.Postgresql:
                    return new SchemaMigration(Version, description,
                        "CREATE TABLE IF NOT EXISTS recovery_state (kind TEXT NOT NULL, state_key TEXT NOT NULL, payload_json TEXT NOT NULL, expires_utc TIMESTAMP NOT NULL, last_update_utc TIMESTAMP NOT NULL, PRIMARY KEY (kind, state_key));",
                        "CREATE INDEX IF NOT EXISTS idx_recovery_state_expires ON recovery_state(expires_utc);");
                case DatabaseTypeEnum.SqlServer:
                    return new SchemaMigration(Version, description,
                        "IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'recovery_state') CREATE TABLE recovery_state (kind NVARCHAR(64) NOT NULL, state_key NVARCHAR(400) NOT NULL, payload_json NVARCHAR(MAX) NOT NULL, expires_utc DATETIME2 NOT NULL, last_update_utc DATETIME2 NOT NULL, CONSTRAINT PK_recovery_state PRIMARY KEY (kind, state_key));",
                        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_recovery_state_expires') CREATE INDEX idx_recovery_state_expires ON recovery_state(expires_utc);");
                case DatabaseTypeEnum.Mysql:
                    return new SchemaMigration(Version, description,
                        "CREATE TABLE IF NOT EXISTS recovery_state (kind VARCHAR(64) NOT NULL, state_key VARCHAR(255) NOT NULL, payload_json LONGTEXT NOT NULL, expires_utc DATETIME(6) NOT NULL, last_update_utc DATETIME(6) NOT NULL, PRIMARY KEY (kind, state_key), INDEX idx_recovery_state_expires (expires_utc));");
                default:
                    throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported database provider.");
            }
        }
    }
}
