namespace Armada.Core.Database
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Migration 140 (RELIABILITY_SIMPLICITY_PLAN.md RS-8.5, RS-8.7): the administrator's tier and the suggested tier on
    /// model_profiles (tier, suggested_tier, suggested_tier_reason, suggested_tier_samples, suggested_tier_utc), and the
    /// Judge strength rule on pipelines and voyages (judge_strength). Every column is nullable; rows stored earlier read as
    /// no tier, no suggestion and the rule off.
    /// </summary>
    internal static class RoutingDataSchema
    {
        internal const int Version = 140;

        internal static SchemaMigration Migration(DatabaseTypeEnum provider)
        {
            const string description = "Add model profile tiers and suggestions, and the Judge strength rule on pipelines and voyages";
            string[] statements;
            switch (provider)
            {
                case DatabaseTypeEnum.Sqlite:
                    statements = new string[]
                    {
                        "ALTER TABLE model_profiles ADD COLUMN tier TEXT NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN suggested_tier TEXT NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN suggested_tier_reason TEXT NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN suggested_tier_samples INTEGER NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN suggested_tier_utc TEXT NULL;",
                        "ALTER TABLE pipelines ADD COLUMN judge_strength TEXT NULL;",
                        "ALTER TABLE voyages ADD COLUMN judge_strength TEXT NULL;"
                    };
                    break;
                case DatabaseTypeEnum.Postgresql:
                    statements = new string[]
                    {
                        "ALTER TABLE model_profiles ADD COLUMN IF NOT EXISTS tier TEXT NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN IF NOT EXISTS suggested_tier TEXT NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN IF NOT EXISTS suggested_tier_reason TEXT NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN IF NOT EXISTS suggested_tier_samples INTEGER NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN IF NOT EXISTS suggested_tier_utc TIMESTAMP NULL;",
                        "ALTER TABLE pipelines ADD COLUMN IF NOT EXISTS judge_strength TEXT NULL;",
                        "ALTER TABLE voyages ADD COLUMN IF NOT EXISTS judge_strength TEXT NULL;"
                    };
                    break;
                case DatabaseTypeEnum.SqlServer:
                    statements = new string[]
                    {
                        "IF COL_LENGTH('model_profiles', 'tier') IS NULL ALTER TABLE model_profiles ADD tier NVARCHAR(32) NULL;",
                        "IF COL_LENGTH('model_profiles', 'suggested_tier') IS NULL ALTER TABLE model_profiles ADD suggested_tier NVARCHAR(32) NULL;",
                        "IF COL_LENGTH('model_profiles', 'suggested_tier_reason') IS NULL ALTER TABLE model_profiles ADD suggested_tier_reason NVARCHAR(1000) NULL;",
                        "IF COL_LENGTH('model_profiles', 'suggested_tier_samples') IS NULL ALTER TABLE model_profiles ADD suggested_tier_samples INT NULL;",
                        "IF COL_LENGTH('model_profiles', 'suggested_tier_utc') IS NULL ALTER TABLE model_profiles ADD suggested_tier_utc NVARCHAR(64) NULL;",
                        "IF COL_LENGTH('pipelines', 'judge_strength') IS NULL ALTER TABLE pipelines ADD judge_strength NVARCHAR(32) NULL;",
                        "IF COL_LENGTH('voyages', 'judge_strength') IS NULL ALTER TABLE voyages ADD judge_strength NVARCHAR(32) NULL;"
                    };
                    break;
                case DatabaseTypeEnum.Mysql:
                    statements = new string[]
                    {
                        "ALTER TABLE model_profiles ADD COLUMN tier VARCHAR(32) NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN suggested_tier VARCHAR(32) NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN suggested_tier_reason VARCHAR(1000) NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN suggested_tier_samples INT NULL;",
                        "ALTER TABLE model_profiles ADD COLUMN suggested_tier_utc DATETIME(6) NULL;",
                        "ALTER TABLE pipelines ADD COLUMN judge_strength VARCHAR(32) NULL;",
                        "ALTER TABLE voyages ADD COLUMN judge_strength VARCHAR(32) NULL;"
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
                return "SELECT COUNT(*) FROM pragma_table_info('voyages') WHERE name = 'judge_strength'";
            if (provider == DatabaseTypeEnum.SqlServer)
                return "SELECT CASE WHEN COL_LENGTH('voyages', 'judge_strength') IS NOT NULL THEN 1 ELSE 0 END";
            string schema = provider == DatabaseTypeEnum.Mysql ? "table_schema = DATABASE()" : "table_schema = current_schema()";
            return "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'voyages' AND column_name = 'judge_strength' AND " + schema;
        }
    }
}
