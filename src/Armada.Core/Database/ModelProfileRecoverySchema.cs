namespace Armada.Core.Database
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Migration 135 (RELIABILITY_SIMPLICITY_PLAN.md RS-5.5): per-model recovery policy overrides on model_profiles.
    /// Every column is nullable; null means the global setting applies.
    /// </summary>
    internal static class ModelProfileRecoverySchema
    {
        #region Internal-Members

        internal const int Version = 135;

        #endregion

        #region Private-Members

        private static readonly string[] _Columns = new string[]
        {
            "recovery_max_resumes", "recovery_unclassified_resumes", "recovery_transient_grace_minutes",
            "recovery_judge_rework_rounds", "recovery_output_repair_attempts"
        };

        #endregion

        #region Internal-Methods

        internal static SchemaMigration Migration(DatabaseTypeEnum provider)
        {
            const string description = "Add per-model recovery policy overrides to model_profiles";
            string[] statements;
            switch (provider)
            {
                case DatabaseTypeEnum.Sqlite:
                    statements = new string[_Columns.Length];
                    for (int i = 0; i < _Columns.Length; i++) statements[i] = "ALTER TABLE model_profiles ADD COLUMN " + _Columns[i] + " INTEGER NULL;";
                    break;
                case DatabaseTypeEnum.Postgresql:
                    statements = new string[] { "ALTER TABLE model_profiles " + Join("ADD COLUMN IF NOT EXISTS ", " INTEGER NULL") + ";" };
                    break;
                case DatabaseTypeEnum.SqlServer:
                    statements = new string[] { "ALTER TABLE model_profiles ADD " + Join(String.Empty, " INT NULL") + ";" };
                    break;
                case DatabaseTypeEnum.Mysql:
                    statements = new string[] { "ALTER TABLE model_profiles " + Join("ADD COLUMN ", " INT NULL") + ";" };
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
            string[] parts = new string[_Columns.Length];
            for (int i = 0; i < _Columns.Length; i++) parts[i] = prefix + _Columns[i] + suffix;
            return String.Join(", ", parts);
        }

        private static string AlreadyAppliedCheck(DatabaseTypeEnum provider)
        {
            string names = "'" + String.Join("','", _Columns) + "'";
            string count = _Columns.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (provider == DatabaseTypeEnum.Sqlite)
                return "SELECT CASE WHEN COUNT(*) = " + count + " THEN 1 ELSE 0 END FROM pragma_table_info('model_profiles') WHERE name IN (" + names + ")";
            if (provider == DatabaseTypeEnum.SqlServer)
                return "SELECT CASE WHEN COUNT(*) = " + count + " THEN 1 ELSE 0 END FROM sys.columns WHERE object_id = OBJECT_ID('model_profiles') AND name IN (" + names + ")";
            return "SELECT CASE WHEN COUNT(*) = " + count + " THEN 1 ELSE 0 END FROM information_schema.columns WHERE table_name = 'model_profiles' AND column_name IN (" + names + ") AND "
                + (provider == DatabaseTypeEnum.Mysql ? "table_schema = DATABASE()" : "table_schema = current_schema()");
        }

        #endregion
    }
}
