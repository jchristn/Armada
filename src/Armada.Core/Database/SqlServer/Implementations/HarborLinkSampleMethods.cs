namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQL Server implementation of Harbor link-health sample persistence.
    /// </summary>
    public class HarborLinkSampleMethods : IHarborLinkSampleMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO harbor_link_samples
            (id, harbor_id, bucket_start_utc, heartbeat_count, round_trip_count, round_trip_total_ms, round_trip_max_ms, reconnect_count, last_reconnect_utc, created_utc)
            VALUES
            (@id, @harbor_id, @bucket_start_utc, @heartbeat_count, @round_trip_count, @round_trip_total_ms, @round_trip_max_ms, @reconnect_count, @last_reconnect_utc, @created_utc);";

        private readonly string _ConnectionString;
        private readonly SemaphoreSlim? _WriteLock;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQL Server database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public HarborLinkSampleMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _ConnectionString = driver.ConnectionString;
            _WriteLock = null;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<HarborLinkSample> CreateAsync(HarborLinkSample sample, CancellationToken token = default)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            if (String.IsNullOrEmpty(sample.HarborId)) throw new ArgumentException("HarborId is required.", nameof(sample));
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, sample), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return sample;
        }

        /// <inheritdoc />
        public async Task<List<HarborLinkSample>> EnumerateAsync(string harborId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            return await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_link_samples WHERE harbor_id = @harbor_id AND bucket_start_utc >= @from_utc AND bucket_start_utc < @to_utc ORDER BY bucket_start_utc ASC, id ASC;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@harbor_id", harborId);
                    SqlServerCommandHelper.AddDate(cmd, "@from_utc", fromUtc);
                    SqlServerCommandHelper.AddDate(cmd, "@to_utc", toUtc);
                }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<HarborLinkSample?> ReadLatestAsync(string harborId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            List<HarborLinkSample> rows = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_link_samples WHERE harbor_id = @harbor_id ORDER BY bucket_start_utc DESC, id DESC" + SqlServerCommandHelper.Page(0, 1) + ";",
                cmd => SqlServerCommandHelper.Add(cmd, "@harbor_id", harborId), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<int> DeleteBeforeAsync(DateTime cutoffUtc, CancellationToken token = default)
        {
            int deleted = 0;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                deleted = await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM harbor_link_samples WHERE bucket_start_utc < @cutoff;",
                    cmd => SqlServerCommandHelper.AddDate(cmd, "@cutoff", cutoffUtc), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        /// <inheritdoc />
        public async Task<int> DeleteByHarborAsync(string harborId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            int deleted = 0;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                deleted = await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM harbor_link_samples WHERE harbor_id = @harbor_id;",
                    cmd => SqlServerCommandHelper.Add(cmd, "@harbor_id", harborId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqlCommand cmd, HarborLinkSample sample)
        {
            SqlServerCommandHelper.Add(cmd, "@id", sample.Id);
            SqlServerCommandHelper.Add(cmd, "@harbor_id", sample.HarborId);
            SqlServerCommandHelper.AddDate(cmd, "@bucket_start_utc", sample.BucketStartUtc);
            SqlServerCommandHelper.Add(cmd, "@heartbeat_count", sample.HeartbeatCount);
            SqlServerCommandHelper.Add(cmd, "@round_trip_count", sample.RoundTripCount);
            SqlServerCommandHelper.Add(cmd, "@round_trip_total_ms", sample.RoundTripTotalMs);
            SqlServerCommandHelper.Add(cmd, "@round_trip_max_ms", sample.RoundTripMaxMs);
            SqlServerCommandHelper.Add(cmd, "@reconnect_count", sample.ReconnectCount);
            SqlServerCommandHelper.AddDate(cmd, "@last_reconnect_utc", sample.LastReconnectUtc);
            SqlServerCommandHelper.AddDate(cmd, "@created_utc", sample.CreatedUtc);
        }

        private static HarborLinkSample FromReader(SqlDataReader reader)
        {
            HarborLinkSample sample = new HarborLinkSample();
            sample.Id = reader["id"].ToString()!;
            sample.HarborId = reader["harbor_id"]?.ToString() ?? String.Empty;
            sample.BucketStartUtc = SqlServerCommandHelper.ReadDate(reader["bucket_start_utc"]);
            sample.HeartbeatCount = SqlServerCommandHelper.ReadInt(reader["heartbeat_count"], 0);
            sample.RoundTripCount = SqlServerCommandHelper.ReadInt(reader["round_trip_count"], 0);
            sample.RoundTripTotalMs = SqlServerCommandHelper.ReadNullableLong(reader["round_trip_total_ms"]) ?? 0;
            sample.RoundTripMaxMs = SqlServerCommandHelper.ReadNullableLong(reader["round_trip_max_ms"]);
            sample.ReconnectCount = SqlServerCommandHelper.ReadNullableInt(reader["reconnect_count"]);
            sample.LastReconnectUtc = SqlServerCommandHelper.ReadNullableDate(reader["last_reconnect_utc"]);
            sample.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            return sample;
        }

        #endregion
    }
}
