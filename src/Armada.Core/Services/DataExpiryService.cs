namespace Armada.Core.Services
{
    using System;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Microsoft.Data.Sqlite;
    using MySqlConnector;
    using Npgsql;
    using SyslogLogging;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;

    /// <summary>
    /// Background service that purges old completed data from the active database.
    /// Removes completed voyages, their missions, old signals, and old events
    /// that exceed the configured retention period. On SQLite each DELETE enters the database's write gate on its own,
    /// so other writers interleave between the statements instead of waiting for the whole sweep.
    /// </summary>
    public class DataExpiryService
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private string _Header = "[DataExpiryService] ";
        private LoggingModule _Logging;
        private string _ConnectionString;
        private DatabaseTypeEnum _Provider;
        private SqliteWriteGate? _WriteGate;
        private int _RetentionDays;

        private static readonly string _Iso8601Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

        private const string _ExpiredVoyages =
            @"SELECT id FROM voyages
                        WHERE status IN ('Complete', 'Cancelled')
                        AND completed_utc IS NOT NULL
                        AND completed_utc < @cutoff";

        private const string _EventDeleteSql =
            @"DELETE FROM events
                    WHERE created_utc < @cutoff
                    AND id NOT IN (
                        SELECT keeper_id FROM (
                            SELECT ranked.id AS keeper_id
                            FROM events AS ranked
                            INNER JOIN (
                                SELECT entity_id, MAX(created_utc) AS max_created
                                FROM events
                                WHERE event_type = 'objective.snapshot'
                                AND entity_id IS NOT NULL
                                AND entity_id <> ''
                                GROUP BY entity_id
                            ) AS newest
                                ON ranked.entity_id = newest.entity_id
                                AND ranked.created_utc = newest.max_created
                                AND ranked.event_type = 'objective.snapshot'
                            WHERE ranked.id = (
                                SELECT MAX(tie.id)
                                FROM events AS tie
                                WHERE tie.event_type = 'objective.snapshot'
                                AND tie.entity_id = ranked.entity_id
                                AND tie.created_utc = ranked.created_utc
                            )
                        ) AS keepers
                    );";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="connectionString">Connection string for the active database.</param>
        /// <param name="retentionDays">Number of days to retain completed data. Set to 0 to disable.</param>
        public DataExpiryService(LoggingModule logging, string connectionString, int retentionDays)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _Provider = DetectProvider(connectionString);
            if (_Provider == DatabaseTypeEnum.Sqlite)
                _WriteGate = SqliteWriteGate.ForConnectionString(connectionString);
            _RetentionDays = retentionDays;
        }

        #endregion

        /// <summary>Active retention period in days. Zero disables expiry; negative values are rejected.</summary>
        public int RetentionDays
        {
            get { return _RetentionDays; }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Retention days must be non-negative.");
                _RetentionDays = value;
            }
        }

        #region Public-Methods

        /// <summary>
        /// Run the data expiry process. Deletes completed voyages (and their missions),
        /// old signals, and old events that are older than the retention period.
        /// The newest objective.snapshot event for each objective is kept.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Total number of rows deleted.</returns>
        public async Task<int> PurgeExpiredDataAsync(CancellationToken token = default)
        {
            if (_RetentionDays <= 0)
            {
                return 0;
            }

            DateTime cutoff = DateTime.UtcNow.AddDays(-_RetentionDays);
            string cutoffStr = cutoff.ToString(_Iso8601Format, CultureInfo.InvariantCulture);
            int totalDeleted = 0;

            _Logging.Debug(_Header + "purging data older than " + cutoffStr);

            using (DbConnection conn = OpenConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                if (_Provider == DatabaseTypeEnum.SqlServer)
                {
                    await ExecuteStatementAsync(conn, ClearParentSql("voyage_id IN (" + _ExpiredVoyages + ")"), "missions", cutoff, token).ConfigureAwait(false);
                }

                // Delete missions belonging to completed/cancelled voyages older than retention
                totalDeleted += await ExecuteStatementAsync(conn,
                    @"DELETE FROM missions WHERE voyage_id IN (
                        " + _ExpiredVoyages + @"
                    );",
                    "missions", cutoff, token).ConfigureAwait(false);

                // Delete completed/cancelled voyages older than retention
                totalDeleted += await ExecuteStatementAsync(conn,
                    @"DELETE FROM voyages
                    WHERE status IN ('Complete', 'Cancelled')
                    AND completed_utc IS NOT NULL
                    AND completed_utc < @cutoff;",
                    "voyages", cutoff, token).ConfigureAwait(false);

                if (_Provider == DatabaseTypeEnum.SqlServer)
                {
                    await ExecuteStatementAsync(conn, ClearParentSql(
                        @"voyage_id IS NULL
                        AND status IN ('Complete', 'Failed', 'Cancelled')
                        AND completed_utc IS NOT NULL
                        AND completed_utc < @cutoff"), "missions", cutoff, token).ConfigureAwait(false);
                }

                // Delete standalone completed/failed/cancelled missions older than retention (no voyage)
                totalDeleted += await ExecuteStatementAsync(conn,
                    @"DELETE FROM missions
                    WHERE voyage_id IS NULL
                    AND status IN ('Complete', 'Failed', 'Cancelled')
                    AND completed_utc IS NOT NULL
                    AND completed_utc < @cutoff;",
                    "missions", cutoff, token).ConfigureAwait(false);

                // Delete the stage outcomes of the missions just removed (and of any mission removed before outcomes were
                // cleaned up with it); they hold no data that outlives the mission.
                totalDeleted += await ExecuteStatementAsync(conn,
                    @"DELETE FROM stage_outcomes
                    WHERE mission_id NOT IN (SELECT id FROM missions);",
                    "", cutoff, token).ConfigureAwait(false);

                // Handoff artifacts expire with their mission, like stage outcomes.
                totalDeleted += await ExecuteDeleteAsync(conn,
                    @"DELETE FROM handoff_artifacts
                    WHERE mission_id NOT IN (SELECT id FROM missions);",
                    cutoffStr, token).ConfigureAwait(false);

                // Delete old read signals
                totalDeleted += await ExecuteStatementAsync(conn,
                    @"DELETE FROM signals
                    WHERE " + ReadColumn() + @" = " + TrueLiteral() + @"
                    AND created_utc < @cutoff;",
                    "signals", cutoff, token).ConfigureAwait(false);

                // Delete old events. Keep the newest objective.snapshot for each objective.
                totalDeleted += await ExecuteStatementAsync(conn, _EventDeleteSql, "events", cutoff, token).ConfigureAwait(false);

                // Delete inactive docks with no captain older than retention
                totalDeleted += await ExecuteStatementAsync(conn,
                    @"DELETE FROM docks
                    WHERE active = " + FalseLiteral() + @"
                    AND captain_id IS NULL
                    AND created_utc < @cutoff;",
                    "docks", cutoff, token).ConfigureAwait(false);

                // Delete old merge entries that are Landed, Cancelled, or Failed
                totalDeleted += await ExecuteStatementAsync(conn,
                    @"DELETE FROM merge_entries
                    WHERE status IN ('Landed', 'Cancelled', 'Failed')
                    AND completed_utc IS NOT NULL
                    AND completed_utc < @cutoff;",
                    "merge_entries", cutoff, token).ConfigureAwait(false);
            }

            if (totalDeleted > 0)
            {
                _Logging.Info(_Header + "purged " + totalDeleted + " expired records");
            }

            return totalDeleted;
        }

        #endregion

        #region Private-Methods

        private DbConnection OpenConnection()
        {
            switch (_Provider)
            {
                case DatabaseTypeEnum.Postgresql:
                    return new NpgsqlConnection(_ConnectionString);
                case DatabaseTypeEnum.Mysql:
                    return new MySqlConnection(_ConnectionString);
                case DatabaseTypeEnum.SqlServer:
                    return new SqlConnection(_ConnectionString);
                default:
                    return new SqliteProviderConnection(_ConnectionString);
            }
        }

        private async Task<int> ExecuteStatementAsync(DbConnection conn, string sql, string timestampTable, DateTime cutoff, CancellationToken token)
        {
            if (_WriteGate != null)
            {
                using (SqliteWriteLease writeLease = await _WriteGate.EnterAsync(token).ConfigureAwait(false))
                {
                    return await ExecuteCommandAsync(conn, sql, timestampTable, cutoff, token).ConfigureAwait(false);
                }
            }

            return await ExecuteCommandAsync(conn, sql, timestampTable, cutoff, token).ConfigureAwait(false);
        }

        private async Task<int> ExecuteCommandAsync(DbConnection conn, string sql, string timestampTable, DateTime cutoff, CancellationToken token)
        {
            using (DbCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                if (sql.IndexOf("@cutoff", StringComparison.Ordinal) >= 0)
                {
                    DbParameter parameter = cmd.CreateParameter();
                    parameter.ParameterName = "@cutoff";
                    parameter.Value = CutoffValue(timestampTable, cutoff);
                    cmd.Parameters.Add(parameter);
                }

                int affected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return affected;
            }
        }

        private object CutoffValue(string timestampTable, DateTime cutoff)
        {
            if (StoresTextTimestamps(timestampTable))
                return cutoff.ToString(_Iso8601Format, CultureInfo.InvariantCulture);
            if (cutoff.Kind == DateTimeKind.Utc)
                return cutoff;
            return DateTime.SpecifyKind(cutoff.ToUniversalTime(), DateTimeKind.Utc);
        }

        private bool StoresTextTimestamps(string table)
        {
            switch (_Provider)
            {
                case DatabaseTypeEnum.Sqlite:
                    return true;
                case DatabaseTypeEnum.SqlServer:
                    return true;
                case DatabaseTypeEnum.Postgresql:
                    return table == "signals" || table == "events" || table == "merge_entries";
                case DatabaseTypeEnum.Mysql:
                    return false;
                default:
                    return true;
            }
        }

        private string ReadColumn()
        {
            switch (_Provider)
            {
                case DatabaseTypeEnum.Mysql:
                    return "`read`";
                case DatabaseTypeEnum.SqlServer:
                    return "[read]";
                default:
                    return "read";
            }
        }

        private string TrueLiteral()
        {
            if (_Provider == DatabaseTypeEnum.Postgresql)
                return "TRUE";
            return "1";
        }

        private string FalseLiteral()
        {
            if (_Provider == DatabaseTypeEnum.Postgresql)
                return "FALSE";
            return "0";
        }

        private static string ClearParentSql(string expiredMissionPredicate)
        {
            return "UPDATE missions SET parent_mission_id = NULL WHERE parent_mission_id IN (SELECT id FROM (SELECT id FROM missions WHERE "
                + expiredMissionPredicate + ") AS expired_missions);";
        }

        private static DatabaseTypeEnum DetectProvider(string connectionString)
        {
            if (HasKey(connectionString, "Uid"))
                return DatabaseTypeEnum.Mysql;
            if (HasKey(connectionString, "User Id"))
                return DatabaseTypeEnum.SqlServer;
            if (HasKey(connectionString, "Host"))
                return DatabaseTypeEnum.Postgresql;
            return DatabaseTypeEnum.Sqlite;
        }

        private static bool HasKey(string connectionString, string key)
        {
            string[] parts = connectionString.Split(';');
            foreach (string part in parts)
            {
                int separator = part.IndexOf('=');
                if (separator <= 0)
                    continue;
                string name = part.Substring(0, separator).Trim();
                if (String.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        #endregion
    }
}
