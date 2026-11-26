namespace Armada.Core.Database
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Migration 143 (RELIABILITY_SIMPLICITY_PLAN.md RS-10.2): an index on missions(status, last_update_utc) so sweeps
    /// that read recently updated missions in one status (the recovery coordinator's Failed and LandingFailed scan) use
    /// an index range instead of loading every row in that status.
    /// </summary>
    internal static class TimeBoundedScanSchema
    {
        internal const int Version = 143;

        private const string _IndexName = "idx_missions_status_last_update";

        internal static SchemaMigration Migration(DatabaseTypeEnum provider)
        {
            const string description = "Index missions by status and last update time for time-bounded sweeps";
            string statement;
            switch (provider)
            {
                case DatabaseTypeEnum.Sqlite:
                case DatabaseTypeEnum.Postgresql:
                    statement = "CREATE INDEX IF NOT EXISTS " + _IndexName + " ON missions(status, last_update_utc);";
                    break;
                case DatabaseTypeEnum.SqlServer:
                    statement = "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='" + _IndexName + "' AND object_id=OBJECT_ID('missions')) "
                        + "CREATE INDEX " + _IndexName + " ON missions(status, last_update_utc);";
                    break;
                case DatabaseTypeEnum.Mysql:
                    statement = "CREATE INDEX " + _IndexName + " ON missions(status, last_update_utc);";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported database provider.");
            }

            return new SchemaMigration(Version, description, new string[] { statement }) { AlreadyAppliedCheckSql = AlreadyAppliedCheck(provider) };
        }

        private static string AlreadyAppliedCheck(DatabaseTypeEnum provider)
        {
            if (provider == DatabaseTypeEnum.Sqlite)
                return "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = '" + _IndexName + "'";
            if (provider == DatabaseTypeEnum.SqlServer)
                return "SELECT COUNT(*) FROM sys.indexes WHERE name = '" + _IndexName + "' AND object_id = OBJECT_ID('missions')";
            if (provider == DatabaseTypeEnum.Mysql)
                return "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = 'missions' AND index_name = '" + _IndexName + "'";
            return "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = current_schema() AND indexname = '" + _IndexName + "'";
        }
    }
}
