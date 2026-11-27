namespace Armada.Core.Database
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Migration 142 (RELIABILITY_SIMPLICITY_PLAN.md RS-8.1): what the latest model probe measured about a profile's tool
    /// support (model_profiles.probed_tools_available, probed_mcp_available, capabilities_probed_utc), kept beside the
    /// administrator's values so a manual value always wins and a later probe can correct its own. Rows stored earlier keep
    /// null values.
    /// </summary>
    internal static class ProbedCapabilitiesSchema
    {
        internal const int Version = 142;

        internal static SchemaMigration Migration(DatabaseTypeEnum provider)
        {
            const string description = "Add the probed tool and MCP capabilities to model profiles";
            string[] statements;
            switch (provider)
            {
                case DatabaseTypeEnum.Sqlite:
                    statements = new string[]
                    {
                        "ALTER TABLE model_profiles ADD COLUMN probed_tools_available INTEGER NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN probed_mcp_available INTEGER NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN capabilities_probed_utc TEXT NULL;"
                    };
                    break;
                case DatabaseTypeEnum.Postgresql:
                    statements = new string[]
                    {
                        "ALTER TABLE model_profiles ADD COLUMN IF NOT EXISTS probed_tools_available BOOLEAN NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN IF NOT EXISTS probed_mcp_available BOOLEAN NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN IF NOT EXISTS capabilities_probed_utc TIMESTAMP NULL;"
                    };
                    break;
                case DatabaseTypeEnum.SqlServer:
                    statements = new string[]
                    {
                        "IF COL_LENGTH('model_profiles', 'probed_tools_available') IS NULL ALTER TABLE model_profiles ADD probed_tools_available BIT NULL;",
                        "IF COL_LENGTH('model_profiles', 'probed_mcp_available') IS NULL ALTER TABLE model_profiles ADD probed_mcp_available BIT NULL;",
                        "IF COL_LENGTH('model_profiles', 'capabilities_probed_utc') IS NULL ALTER TABLE model_profiles ADD capabilities_probed_utc DATETIME2 NULL;"
                    };
                    break;
                case DatabaseTypeEnum.Mysql:
                    statements = new string[]
                    {
                        "ALTER TABLE model_profiles ADD COLUMN probed_tools_available TINYINT(1) NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN probed_mcp_available TINYINT(1) NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN capabilities_probed_utc DATETIME(6) NULL;"
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
                return "SELECT COUNT(*) FROM pragma_table_info('model_profiles') WHERE name = 'capabilities_probed_utc'";
            if (provider == DatabaseTypeEnum.SqlServer)
                return "SELECT CASE WHEN COL_LENGTH('model_profiles', 'capabilities_probed_utc') IS NOT NULL THEN 1 ELSE 0 END";
            string schema = provider == DatabaseTypeEnum.Mysql ? "table_schema = DATABASE()" : "table_schema = current_schema()";
            return "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'model_profiles' AND column_name = 'capabilities_probed_utc' AND " + schema;
        }
    }
}
