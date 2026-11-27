namespace Armada.Core.Database
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Migration 141 (RELIABILITY_SIMPLICITY_PLAN.md RS-8.8): per-model timeout overrides and the newest measured speed
    /// on model_profiles. Every column is nullable; a null override means the measured-speed default or the global
    /// setting applies.
    /// </summary>
    internal static class ModelProfileTimeoutsSchema
    {
        #region Internal-Members

        internal const int Version = 141;

        #endregion

        #region Private-Members

        private static readonly string[] _IntColumns = new string[]
        {
            "timeout_stall_minutes", "timeout_tool_wait_minutes", "timeout_max_runtime_minutes", "timeout_first_output_minutes"
        };

        #endregion

        #region Internal-Methods

        internal static SchemaMigration Migration(DatabaseTypeEnum provider)
        {
            const string description = "Add per-model timeout overrides and measured speed to model_profiles";
            string[] statements;
            switch (provider)
            {
                case DatabaseTypeEnum.Sqlite:
                    statements = new string[_IntColumns.Length + 3];
                    for (int i = 0; i < _IntColumns.Length; i++) statements[i] = "ALTER TABLE model_profiles ADD COLUMN " + _IntColumns[i] + " INTEGER NULL;";
                    statements[_IntColumns.Length] = "ALTER TABLE model_profiles ADD COLUMN speed_tokens_per_second REAL NULL;";
                    statements[_IntColumns.Length + 1] = "ALTER TABLE model_profiles ADD COLUMN speed_first_output_ms INTEGER NULL;";
                    statements[_IntColumns.Length + 2] = "ALTER TABLE model_profiles ADD COLUMN speed_measured_utc TEXT NULL;";
                    break;
                case DatabaseTypeEnum.Postgresql:
                    statements = new string[]
                    {
                        "ALTER TABLE model_profiles " + Join("ADD COLUMN IF NOT EXISTS ", " INTEGER NULL")
                            + ", ADD COLUMN IF NOT EXISTS speed_tokens_per_second DOUBLE PRECISION NULL"
                            + ", ADD COLUMN IF NOT EXISTS speed_first_output_ms BIGINT NULL"
                            + ", ADD COLUMN IF NOT EXISTS speed_measured_utc TIMESTAMP NULL;"
                    };
                    break;
                case DatabaseTypeEnum.SqlServer:
                    statements = new string[]
                    {
                        "ALTER TABLE model_profiles ADD " + Join(String.Empty, " INT NULL")
                            + ", speed_tokens_per_second FLOAT NULL, speed_first_output_ms BIGINT NULL, speed_measured_utc DATETIME2 NULL;"
                    };
                    break;
                case DatabaseTypeEnum.Mysql:
                    statements = new string[]
                    {
                        "ALTER TABLE model_profiles " + Join("ADD COLUMN ", " INT NULL")
                            + ", ADD COLUMN speed_tokens_per_second DOUBLE NULL, ADD COLUMN speed_first_output_ms BIGINT NULL, ADD COLUMN speed_measured_utc DATETIME(6) NULL;"
                    };
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported database provider.");
            }

            return new SchemaMigration(Version, description, statements) { AlreadyAppliedCheckSql = AlreadyAppliedCheck(provider) };
        }

        #endregion

        #region Private-Methods

        private static string Join(string prefix, string suffix)
        {
            string[] parts = new string[_IntColumns.Length];
            for (int i = 0; i < _IntColumns.Length; i++) parts[i] = prefix + _IntColumns[i] + suffix;
            return String.Join(", ", parts);
        }

        private static string AlreadyAppliedCheck(DatabaseTypeEnum provider)
        {
            if (provider == DatabaseTypeEnum.Sqlite)
                return "SELECT COUNT(*) FROM pragma_table_info('model_profiles') WHERE name = 'speed_measured_utc'";
            if (provider == DatabaseTypeEnum.SqlServer)
                return "SELECT CASE WHEN COL_LENGTH('model_profiles', 'speed_measured_utc') IS NOT NULL THEN 1 ELSE 0 END";
            return "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'model_profiles' AND column_name = 'speed_measured_utc' AND "
                + (provider == DatabaseTypeEnum.Mysql ? "table_schema = DATABASE()" : "table_schema = current_schema()");
        }

        #endregion
    }
}
