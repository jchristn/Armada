namespace Armada.Core.Database
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Migration 138 (RELIABILITY_SIMPLICITY_PLAN.md RS-6.2): the stage_submissions table, which keeps the plans, verdicts and
    /// results captains submit through the submit tools, one row per accepted submission, keyed by mission and attempt.
    /// </summary>
    internal static class StageSubmissionTableSchema
    {
        internal const int Version = 138;

        internal static SchemaMigration Migration(DatabaseTypeEnum provider)
        {
            const string description = "Add stage_submissions for plans, verdicts and results submitted through the submit tools";
            switch (provider)
            {
                case DatabaseTypeEnum.Sqlite:
                    return new SchemaMigration(Version, description,
                        "CREATE TABLE IF NOT EXISTS stage_submissions (id TEXT PRIMARY KEY, tenant_id TEXT NULL, mission_id TEXT NOT NULL, attempt_id TEXT NOT NULL, persona TEXT NOT NULL, kind TEXT NOT NULL, source TEXT NOT NULL, payload_json TEXT NOT NULL, created_utc TEXT NOT NULL);",
                        "CREATE INDEX IF NOT EXISTS idx_stage_submissions_mission_attempt ON stage_submissions(mission_id, attempt_id);");
                case DatabaseTypeEnum.Postgresql:
                    return new SchemaMigration(Version, description,
                        "CREATE TABLE IF NOT EXISTS stage_submissions (id TEXT PRIMARY KEY, tenant_id TEXT NULL, mission_id TEXT NOT NULL, attempt_id TEXT NOT NULL, persona TEXT NOT NULL, kind TEXT NOT NULL, source TEXT NOT NULL, payload_json TEXT NOT NULL, created_utc TIMESTAMP NOT NULL);",
                        "CREATE INDEX IF NOT EXISTS idx_stage_submissions_mission_attempt ON stage_submissions(mission_id, attempt_id);");
                case DatabaseTypeEnum.SqlServer:
                    return new SchemaMigration(Version, description,
                        "IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'stage_submissions') CREATE TABLE stage_submissions (id NVARCHAR(64) NOT NULL PRIMARY KEY, tenant_id NVARCHAR(64) NULL, mission_id NVARCHAR(64) NOT NULL, attempt_id NVARCHAR(128) NOT NULL, persona NVARCHAR(128) NOT NULL, kind NVARCHAR(32) NOT NULL, source NVARCHAR(32) NOT NULL, payload_json NVARCHAR(MAX) NOT NULL, created_utc DATETIME2 NOT NULL);",
                        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_stage_submissions_mission_attempt') CREATE INDEX idx_stage_submissions_mission_attempt ON stage_submissions(mission_id, attempt_id);");
                case DatabaseTypeEnum.Mysql:
                    return new SchemaMigration(Version, description,
                        "CREATE TABLE IF NOT EXISTS stage_submissions (id VARCHAR(64) NOT NULL PRIMARY KEY, tenant_id VARCHAR(64) NULL, mission_id VARCHAR(64) NOT NULL, attempt_id VARCHAR(128) NOT NULL, persona VARCHAR(128) NOT NULL, kind VARCHAR(32) NOT NULL, source VARCHAR(32) NOT NULL, payload_json LONGTEXT NOT NULL, created_utc DATETIME(6) NOT NULL, INDEX idx_stage_submissions_mission_attempt (mission_id, attempt_id));");
                default:
                    throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported database provider.");
            }
        }
    }
}
