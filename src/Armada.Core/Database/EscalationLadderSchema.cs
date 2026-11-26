namespace Armada.Core.Database
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Migration 136 (RELIABILITY_SIMPLICITY_PLAN.md RS-5.6): the persona escalation ladder as relational child rows
    /// (persona_escalation_rungs, shaped like persona_captain_priorities) and the "escalate after" resume count on
    /// model_profiles (nullable; null means escalate only when the resumes are spent).
    /// </summary>
    internal static class EscalationLadderSchema
    {
        internal const int Version = 136;

        internal static SchemaMigration Migration(DatabaseTypeEnum provider)
        {
            const string description = "Add persona escalation ladder rungs and the model profile escalate-after resume count";
            string[] statements;
            switch (provider)
            {
                case DatabaseTypeEnum.Sqlite:
                    statements = new string[]
                    {
                        Table("CREATE TABLE IF NOT EXISTS", "TEXT", "TEXT", "TEXT"),
                        "ALTER TABLE model_profiles ADD COLUMN recovery_escalate_after_resumes INTEGER NULL;"
                    };
                    break;
                case DatabaseTypeEnum.Postgresql:
                    statements = new string[]
                    {
                        Table("CREATE TABLE IF NOT EXISTS", "TEXT", "TEXT", "TEXT"),
                        "ALTER TABLE model_profiles ADD COLUMN IF NOT EXISTS recovery_escalate_after_resumes INTEGER NULL;"
                    };
                    break;
                case DatabaseTypeEnum.SqlServer:
                    statements = new string[]
                    {
                        "IF OBJECT_ID('persona_escalation_rungs', 'U') IS NULL " + Table("CREATE TABLE", "NVARCHAR(450)", "VARCHAR(64)", "NVARCHAR(1024)"),
                        "IF COL_LENGTH('model_profiles', 'recovery_escalate_after_resumes') IS NULL ALTER TABLE model_profiles ADD recovery_escalate_after_resumes INT NULL;"
                    };
                    break;
                case DatabaseTypeEnum.Mysql:
                    statements = new string[]
                    {
                        Table("CREATE TABLE IF NOT EXISTS", "VARCHAR(450)", "VARCHAR(64)", "VARCHAR(1024)"),
                        "ALTER TABLE model_profiles ADD COLUMN recovery_escalate_after_resumes INT NULL;"
                    };
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported database provider.");
            }

            return new SchemaMigration(Version, description, statements) { AlreadyAppliedCheckSql = AlreadyAppliedCheck(provider) };
        }

        private static string Table(string create, string idType, string enumType, string matchType)
        {
            return create + " persona_escalation_rungs ("
                + "persona_id " + idType + " NOT NULL, "
                + "rung_order INTEGER NOT NULL, "
                + "runtime " + enumType + " NULL, "
                + "model " + matchType + " NULL, "
                + "endpoint " + matchType + " NULL, "
                + "minimum_tier " + enumType + " NULL, "
                + "PRIMARY KEY (persona_id, rung_order), "
                + "FOREIGN KEY (persona_id) REFERENCES personas(id) ON DELETE CASCADE);";
        }

        private static string AlreadyAppliedCheck(DatabaseTypeEnum provider)
        {
            if (provider == DatabaseTypeEnum.Sqlite)
                return "SELECT CASE WHEN (SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'persona_escalation_rungs') = 1 "
                    + "AND (SELECT COUNT(*) FROM pragma_table_info('model_profiles') WHERE name = 'recovery_escalate_after_resumes') = 1 THEN 1 ELSE 0 END";
            if (provider == DatabaseTypeEnum.SqlServer)
                return "SELECT CASE WHEN OBJECT_ID('persona_escalation_rungs', 'U') IS NOT NULL AND COL_LENGTH('model_profiles', 'recovery_escalate_after_resumes') IS NOT NULL THEN 1 ELSE 0 END";
            string schema = provider == DatabaseTypeEnum.Mysql ? "table_schema = DATABASE()" : "table_schema = current_schema()";
            return "SELECT CASE WHEN (SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'persona_escalation_rungs' AND " + schema + ") = 1 "
                + "AND (SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'model_profiles' AND column_name = 'recovery_escalate_after_resumes' AND " + schema + ") = 1 THEN 1 ELSE 0 END";
        }
    }
}
