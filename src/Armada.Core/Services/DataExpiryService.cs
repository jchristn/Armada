namespace Armada.Core.Services
{
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using SyslogLogging;

    /// <summary>
    /// Background service that purges old completed data from the database.
    /// Removes completed voyages, their missions, old signals, and old events
    /// that exceed the configured retention period.
    /// </summary>
    public class DataExpiryService
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private string _Header = "[DataExpiryService] ";
        private LoggingModule _Logging;
        private string _ConnectionString;
        private int _RetentionDays;

        private static readonly string _Iso8601Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="connectionString">SQLite connection string.</param>
        /// <param name="retentionDays">Number of days to retain completed data. Set to 0 to disable.</param>
        public DataExpiryService(LoggingModule logging, string connectionString, int retentionDays)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _RetentionDays = retentionDays;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the data expiry process. Deletes completed voyages (and their missions),
        /// old signals, and old events that are older than the retention period.
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

            _Logging.Info(_Header + "purging data older than " + cutoffStr);

            using (SqliteConnection conn = new SqliteConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                // Delete missions belonging to completed/cancelled voyages older than retention
                totalDeleted += await ExecuteDeleteAsync(conn,
                    @"DELETE FROM missions WHERE voyage_id IN (
                        SELECT id FROM voyages
                        WHERE status IN ('Complete', 'Cancelled')
                        AND completed_utc IS NOT NULL
                        AND completed_utc < @cutoff
                    );",
                    cutoffStr, token).ConfigureAwait(false);

                // Delete completed/cancelled voyages older than retention
                totalDeleted += await ExecuteDeleteAsync(conn,
                    @"DELETE FROM voyages
                    WHERE status IN ('Complete', 'Cancelled')
                    AND completed_utc IS NOT NULL
                    AND completed_utc < @cutoff;",
                    cutoffStr, token).ConfigureAwait(false);

                // Delete standalone completed/failed/cancelled missions older than retention (no voyage)
                totalDeleted += await ExecuteDeleteAsync(conn,
                    @"DELETE FROM missions
                    WHERE voyage_id IS NULL
                    AND status IN ('Complete', 'Failed', 'Cancelled')
                    AND completed_utc IS NOT NULL
                    AND completed_utc < @cutoff;",
                    cutoffStr, token).ConfigureAwait(false);

                // Delete old read signals
                totalDeleted += await ExecuteDeleteAsync(conn,
                    @"DELETE FROM signals
                    WHERE read = 1
                    AND created_utc < @cutoff;",
                    cutoffStr, token).ConfigureAwait(false);

                // Delete old events
                totalDeleted += await ExecuteDeleteAsync(conn,
                    @"DELETE FROM events
                    WHERE created_utc < @cutoff;",
                    cutoffStr, token).ConfigureAwait(false);

                // Delete inactive docks with no captain older than retention
                totalDeleted += await ExecuteDeleteAsync(conn,
                    @"DELETE FROM docks
                    WHERE active = 0
                    AND captain_id IS NULL
                    AND created_utc < @cutoff;",
                    cutoffStr, token).ConfigureAwait(false);

                // Delete old merge entries that are Landed, Cancelled, or Failed
                totalDeleted += await ExecuteDeleteAsync(conn,
                    @"DELETE FROM merge_entries
                    WHERE status IN ('Landed', 'Cancelled', 'Failed')
                    AND completed_utc IS NOT NULL
                    AND completed_utc < @cutoff;",
                    cutoffStr, token).ConfigureAwait(false);
            }

            if (totalDeleted > 0)
            {
                _Logging.Info(_Header + "purged " + totalDeleted + " expired records");
            }

            return totalDeleted;
        }

        #endregion

        #region Private-Methods

        private async Task<int> ExecuteDeleteAsync(SqliteConnection conn, string sql, string cutoff, CancellationToken token)
        {
            using (SqliteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@cutoff", cutoff);
                int affected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return affected;
            }
        }

        #endregion
    }
}
