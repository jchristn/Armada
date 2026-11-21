namespace Armada.Core.Database
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Storage of where an Ask Armada conversation runs (migration 132, every provider): the ask_threads.harbor_id
    /// and ask_threads.working_directory columns, read into <see cref="AskThread.HarborId"/> and
    /// <see cref="AskThread.WorkingDirectory"/>.
    /// </summary>
    internal static class AskThreadLocationSchema
    {
        #region Internal-Members

        /// <summary>
        /// Migration version.
        /// </summary>
        internal const int Version = 132;

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Migration 132 for a provider: add harbor_id and working_directory to ask_threads.
        /// </summary>
        /// <param name="provider">Database provider.</param>
        /// <returns>The migration.</returns>
        internal static SchemaMigration Migration(DatabaseTypeEnum provider)
        {
            const string description = "Add harbor_id and working_directory to ask_threads so a conversation can run on a Harbor in a chosen folder";
            string check = provider == DatabaseTypeEnum.Sqlite
                ? "SELECT CASE WHEN COUNT(*) = 2 THEN 1 ELSE 0 END FROM pragma_table_info('ask_threads') WHERE name IN ('harbor_id','working_directory')"
                : provider == DatabaseTypeEnum.SqlServer
                    ? "SELECT CASE WHEN COUNT(*) = 2 THEN 1 ELSE 0 END FROM sys.columns WHERE object_id = OBJECT_ID('ask_threads') AND name IN ('harbor_id','working_directory')"
                    : "SELECT CASE WHEN COUNT(*) = 2 THEN 1 ELSE 0 END FROM information_schema.columns WHERE table_name = 'ask_threads' AND column_name IN ('harbor_id','working_directory') AND "
                        + (provider == DatabaseTypeEnum.Mysql ? "table_schema = DATABASE()" : "table_schema = current_schema()");

            switch (provider)
            {
                case DatabaseTypeEnum.Sqlite:
                    return new SchemaMigration(Version, description,
                        "ALTER TABLE ask_threads ADD COLUMN harbor_id TEXT NULL;",
                        "ALTER TABLE ask_threads ADD COLUMN working_directory TEXT NULL;") { AlreadyAppliedCheckSql = check };
                case DatabaseTypeEnum.Postgresql:
                    return new SchemaMigration(Version, description,
                        "ALTER TABLE ask_threads ADD COLUMN IF NOT EXISTS harbor_id TEXT NULL, ADD COLUMN IF NOT EXISTS working_directory TEXT NULL;") { AlreadyAppliedCheckSql = check };
                case DatabaseTypeEnum.Mysql:
                    return new SchemaMigration(Version, description,
                        "ALTER TABLE ask_threads ADD COLUMN harbor_id VARCHAR(64) NULL, ADD COLUMN working_directory VARCHAR(1024) NULL;") { AlreadyAppliedCheckSql = check };
                case DatabaseTypeEnum.SqlServer:
                    return new SchemaMigration(Version, description,
                        "ALTER TABLE ask_threads ADD harbor_id NVARCHAR(64) NULL, working_directory NVARCHAR(1024) NULL;") { AlreadyAppliedCheckSql = check };
                default:
                    throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported database provider.");
            }
        }

        #endregion
    }
}
