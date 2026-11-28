namespace Armada.Core.Database
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Migration 139 (RELIABILITY_SIMPLICITY_PLAN.md RS-7.8): the context window a model server reports for an endpoint
    /// that configures none (model_endpoints.discovered_context_length, discovered_context_source, context_discovered_utc),
    /// and where a model profile's served context came from (model_profiles.served_context_source). Rows stored earlier
    /// keep null values.
    /// </summary>
    internal static class ContextDiscoverySchema
    {
        internal const int Version = 139;

        internal static SchemaMigration Migration(DatabaseTypeEnum provider)
        {
            const string description = "Add the discovered context window to model endpoints and the served context source to model profiles";
            string[] statements;
            switch (provider)
            {
                case DatabaseTypeEnum.Sqlite:
                    statements = new string[]
                    {
                        "ALTER TABLE model_endpoints ADD COLUMN discovered_context_length INTEGER NULL;",
                        "ALTER TABLE model_endpoints ADD COLUMN discovered_context_source TEXT NULL;",
                        "ALTER TABLE model_endpoints ADD COLUMN context_discovered_utc TEXT NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN served_context_source TEXT NULL;"
                    };
                    break;
                case DatabaseTypeEnum.Postgresql:
                    statements = new string[]
                    {
                        "ALTER TABLE model_endpoints ADD COLUMN IF NOT EXISTS discovered_context_length INTEGER NULL;",
                        "ALTER TABLE model_endpoints ADD COLUMN IF NOT EXISTS discovered_context_source TEXT NULL;",
                        "ALTER TABLE model_endpoints ADD COLUMN IF NOT EXISTS context_discovered_utc TIMESTAMP NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN IF NOT EXISTS served_context_source TEXT NULL;"
                    };
                    break;
                case DatabaseTypeEnum.SqlServer:
                    statements = new string[]
                    {
                        "IF COL_LENGTH('model_endpoints', 'discovered_context_length') IS NULL ALTER TABLE model_endpoints ADD discovered_context_length INT NULL;",
                        "IF COL_LENGTH('model_endpoints', 'discovered_context_source') IS NULL ALTER TABLE model_endpoints ADD discovered_context_source NVARCHAR(128) NULL;",
                        "IF COL_LENGTH('model_endpoints', 'context_discovered_utc') IS NULL ALTER TABLE model_endpoints ADD context_discovered_utc NVARCHAR(64) NULL;",
                        "IF COL_LENGTH('model_profiles', 'served_context_source') IS NULL ALTER TABLE model_profiles ADD served_context_source NVARCHAR(32) NULL;"
                    };
                    break;
                case DatabaseTypeEnum.Mysql:
                    statements = new string[]
                    {
                        "ALTER TABLE model_endpoints ADD COLUMN discovered_context_length INT NULL;",
                        "ALTER TABLE model_endpoints ADD COLUMN discovered_context_source VARCHAR(128) NULL;",
                        "ALTER TABLE model_endpoints ADD COLUMN context_discovered_utc DATETIME(6) NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN served_context_source VARCHAR(32) NULL;"
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
                return "SELECT COUNT(*) FROM pragma_table_info('model_profiles') WHERE name = 'served_context_source'";
            if (provider == DatabaseTypeEnum.SqlServer)
                return "SELECT CASE WHEN COL_LENGTH('model_profiles', 'served_context_source') IS NOT NULL THEN 1 ELSE 0 END";
            string schema = provider == DatabaseTypeEnum.Mysql ? "table_schema = DATABASE()" : "table_schema = current_schema()";
            return "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'model_profiles' AND column_name = 'served_context_source' AND " + schema;
        }
    }
}
