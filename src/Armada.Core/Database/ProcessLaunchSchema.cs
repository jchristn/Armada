namespace Armada.Core.Database
{
    using System;
    using System.Data.Common;
    using System.Globalization;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Storage of the exact start time of a mission's local agent process (migration 131, every provider): the
    /// missions.process_started_pid and missions.process_started_utc columns, written together by
    /// <c>IMissionMethods.SetProcessLaunchAsync</c> and read into <see cref="Mission.ProcessStartedPid"/> and
    /// <see cref="Mission.ProcessStartedUtc"/>. The start time is stored as ISO 8601 UTC text on every provider so it
    /// round-trips at full precision.
    /// </summary>
    internal static class ProcessLaunchSchema
    {
        #region Internal-Members

        /// <summary>
        /// Migration version.
        /// </summary>
        internal const int Version = 131;

        /// <summary>
        /// Text format of process_started_utc.
        /// </summary>
        internal const string Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Migration 131 for a provider: add process_started_pid and process_started_utc to missions in one statement.
        /// </summary>
        /// <param name="provider">Database provider.</param>
        /// <returns>The migration.</returns>
        internal static SchemaMigration Migration(DatabaseTypeEnum provider)
        {
            const string description = "Record the exact start time of a mission's local agent process";
            string check = provider == DatabaseTypeEnum.Sqlite
                ? "SELECT CASE WHEN COUNT(*) = 2 THEN 1 ELSE 0 END FROM pragma_table_info('missions') WHERE name IN ('process_started_pid','process_started_utc')"
                : provider == DatabaseTypeEnum.SqlServer
                    ? "SELECT CASE WHEN COUNT(*) = 2 THEN 1 ELSE 0 END FROM sys.columns WHERE object_id = OBJECT_ID('missions') AND name IN ('process_started_pid','process_started_utc')"
                    : "SELECT CASE WHEN COUNT(*) = 2 THEN 1 ELSE 0 END FROM information_schema.columns WHERE table_name = 'missions' AND column_name IN ('process_started_pid','process_started_utc') AND "
                        + (provider == DatabaseTypeEnum.Mysql ? "table_schema = DATABASE()" : "table_schema = current_schema()");

            switch (provider)
            {
                case DatabaseTypeEnum.Sqlite:
                    return new SchemaMigration(Version, description,
                        "ALTER TABLE missions ADD COLUMN process_started_pid INTEGER NULL;",
                        "ALTER TABLE missions ADD COLUMN process_started_utc TEXT NULL;") { AlreadyAppliedCheckSql = check };
                case DatabaseTypeEnum.Postgresql:
                    return new SchemaMigration(Version, description,
                        "ALTER TABLE missions ADD COLUMN IF NOT EXISTS process_started_pid INTEGER NULL, ADD COLUMN IF NOT EXISTS process_started_utc VARCHAR(40) NULL;") { AlreadyAppliedCheckSql = check };
                case DatabaseTypeEnum.Mysql:
                    return new SchemaMigration(Version, description,
                        "ALTER TABLE missions ADD COLUMN process_started_pid INT NULL, ADD COLUMN process_started_utc VARCHAR(40) NULL;") { AlreadyAppliedCheckSql = check };
                default:
                    return new SchemaMigration(Version, description,
                        "ALTER TABLE missions ADD process_started_pid INT NULL, process_started_utc NVARCHAR(40) NULL;") { AlreadyAppliedCheckSql = check };
            }
        }

        /// <summary>
        /// Text stored for a start time, or null.
        /// </summary>
        /// <param name="startedUtc">Start time, or null.</param>
        /// <returns>ISO 8601 UTC text, or null.</returns>
        internal static string? ToText(DateTime? startedUtc)
        {
            return startedUtc.HasValue ? UtcClock.Format(startedUtc.Value, Format) : null;
        }

        /// <summary>
        /// Read the two columns into a mission when the result set has them (summary queries do not).
        /// </summary>
        /// <param name="reader">Reader positioned on a mission row.</param>
        /// <param name="mission">Mission to fill.</param>
        internal static void Read(DbDataReader reader, Mission mission)
        {
            int pidOrdinal;
            int startOrdinal;
            try
            {
                pidOrdinal = reader.GetOrdinal("process_started_pid");
                startOrdinal = reader.GetOrdinal("process_started_utc");
            }
            catch (IndexOutOfRangeException)
            {
                return;
            }
            catch (ArgumentException)
            {
                return;
            }

            mission.ProcessStartedPid = reader.IsDBNull(pidOrdinal) ? null : Convert.ToInt32(reader.GetValue(pidOrdinal), CultureInfo.InvariantCulture);
            if (reader.IsDBNull(startOrdinal))
            {
                mission.ProcessStartedUtc = null;
                return;
            }

            string text = Convert.ToString(reader.GetValue(startOrdinal), CultureInfo.InvariantCulture) ?? String.Empty;
            mission.ProcessStartedUtc = String.IsNullOrEmpty(text) ? null : UtcClock.Parse(text);
        }

        #endregion
    }
}
