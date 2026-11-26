namespace Armada.Core.Database
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Migration 137 (RELIABILITY_SIMPLICITY_PLAN.md RS-6.9): the attempt id on handoff artifacts, so the artifact written
    /// at stage completion and the stage outcome recorded after the exit find the same row by attempt instead of comparing
    /// output and diff lengths. Rows stored earlier keep a null attempt id.
    /// </summary>
    internal static class HandoffAttemptSchema
    {
        internal const int Version = 137;

        internal static SchemaMigration Migration(DatabaseTypeEnum provider)
        {
            const string description = "Add the attempt id to handoff artifacts";
            string[] statements;
            switch (provider)
            {
                case DatabaseTypeEnum.Sqlite:
                    statements = new string[]
                    {
                        "ALTER TABLE handoff_artifacts ADD COLUMN attempt_id TEXT NULL;",
                        "CREATE INDEX IF NOT EXISTS idx_handoff_artifacts_mission_attempt ON handoff_artifacts(mission_id, attempt_id);"
                    };
                    break;
                case DatabaseTypeEnum.Postgresql:
                    statements = new string[]
                    {
                        "ALTER TABLE handoff_artifacts ADD COLUMN IF NOT EXISTS attempt_id TEXT NULL;",
                        "CREATE INDEX IF NOT EXISTS idx_handoff_artifacts_mission_attempt ON handoff_artifacts(mission_id, attempt_id);"
                    };
                    break;
                case DatabaseTypeEnum.SqlServer:
                    statements = new string[]
                    {
                        "IF COL_LENGTH('handoff_artifacts', 'attempt_id') IS NULL ALTER TABLE handoff_artifacts ADD attempt_id NVARCHAR(128) NULL;",
                        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_handoff_artifacts_mission_attempt') CREATE INDEX idx_handoff_artifacts_mission_attempt ON handoff_artifacts(mission_id, attempt_id);"
                    };
                    break;
                case DatabaseTypeEnum.Mysql:
                    statements = new string[]
                    {
                        "ALTER TABLE handoff_artifacts ADD COLUMN attempt_id VARCHAR(128) NULL;",
                        "CREATE INDEX idx_handoff_artifacts_mission_attempt ON handoff_artifacts(mission_id, attempt_id);"
                    };
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported database provider.");
            }

            return new SchemaMigration(Version, description, statements) { AlreadyAppliedCheckSql = AlreadyAppliedCheck(provider) };
        }

        private static string AlreadyAppliedCheck(DatabaseTypeEnum provider)
        {
            if (provider == DatabaseTypeEnum.Sqlite)
                return "SELECT COUNT(*) FROM pragma_table_info('handoff_artifacts') WHERE name = 'attempt_id'";
            if (provider == DatabaseTypeEnum.SqlServer)
                return "SELECT CASE WHEN COL_LENGTH('handoff_artifacts', 'attempt_id') IS NOT NULL THEN 1 ELSE 0 END";
            string schema = provider == DatabaseTypeEnum.Mysql ? "table_schema = DATABASE()" : "table_schema = current_schema()";
            return "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'handoff_artifacts' AND column_name = 'attempt_id' AND " + schema;
        }
    }
}
