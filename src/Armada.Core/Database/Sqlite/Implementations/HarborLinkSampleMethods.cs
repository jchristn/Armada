namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of Harbor link-health sample persistence.
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
        /// <param name="driver">SQLite database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public HarborLinkSampleMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _ConnectionString = driver.ConnectionString;
            _WriteLock = driver.WriteLock;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<HarborLinkSample> CreateAsync(HarborLinkSample sample, CancellationToken token = default)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            if (String.IsNullOrEmpty(sample.HarborId)) throw new ArgumentException("HarborId is required.", nameof(sample));
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, sample), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return sample;
        }

        /// <inheritdoc />
        public async Task<List<HarborLinkSample>> EnumerateAsync(string harborId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            return await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_link_samples WHERE harbor_id = @harbor_id AND bucket_start_utc >= @from_utc AND bucket_start_utc < @to_utc ORDER BY bucket_start_utc ASC, id ASC;",
                cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@harbor_id", harborId);
                    SqliteCommandHelper.AddDate(cmd, "@from_utc", fromUtc);
                    SqliteCommandHelper.AddDate(cmd, "@to_utc", toUtc);
                }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<HarborLinkSample?> ReadLatestAsync(string harborId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            List<HarborLinkSample> rows = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_link_samples WHERE harbor_id = @harbor_id ORDER BY bucket_start_utc DESC, id DESC" + SqliteCommandHelper.Page(0, 1) + ";",
                cmd => SqliteCommandHelper.Add(cmd, "@harbor_id", harborId), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<int> DeleteBeforeAsync(DateTime cutoffUtc, CancellationToken token = default)
        {
            int deleted = 0;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                deleted = await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM harbor_link_samples WHERE bucket_start_utc < @cutoff;",
                    cmd => SqliteCommandHelper.AddDate(cmd, "@cutoff", cutoffUtc), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        /// <inheritdoc />
        public async Task<int> DeleteByHarborAsync(string harborId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            int deleted = 0;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                deleted = await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM harbor_link_samples WHERE harbor_id = @harbor_id;",
                    cmd => SqliteCommandHelper.Add(cmd, "@harbor_id", harborId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqliteCommand cmd, HarborLinkSample sample)
        {
            SqliteCommandHelper.Add(cmd, "@id", sample.Id);
            SqliteCommandHelper.Add(cmd, "@harbor_id", sample.HarborId);
            SqliteCommandHelper.AddDate(cmd, "@bucket_start_utc", sample.BucketStartUtc);
            SqliteCommandHelper.Add(cmd, "@heartbeat_count", sample.HeartbeatCount);
            SqliteCommandHelper.Add(cmd, "@round_trip_count", sample.RoundTripCount);
            SqliteCommandHelper.Add(cmd, "@round_trip_total_ms", sample.RoundTripTotalMs);
            SqliteCommandHelper.Add(cmd, "@round_trip_max_ms", sample.RoundTripMaxMs);
            SqliteCommandHelper.Add(cmd, "@reconnect_count", sample.ReconnectCount);
            SqliteCommandHelper.AddDate(cmd, "@last_reconnect_utc", sample.LastReconnectUtc);
            SqliteCommandHelper.AddDate(cmd, "@created_utc", sample.CreatedUtc);
        }

        private static HarborLinkSample FromReader(SqliteDataReader reader)
        {
            HarborLinkSample sample = new HarborLinkSample();
            sample.Id = reader["id"].ToString()!;
            sample.HarborId = reader["harbor_id"]?.ToString() ?? String.Empty;
            sample.BucketStartUtc = SqliteCommandHelper.ReadDate(reader["bucket_start_utc"]);
            sample.HeartbeatCount = SqliteCommandHelper.ReadInt(reader["heartbeat_count"], 0);
            sample.RoundTripCount = SqliteCommandHelper.ReadInt(reader["round_trip_count"], 0);
            sample.RoundTripTotalMs = SqliteCommandHelper.ReadNullableLong(reader["round_trip_total_ms"]) ?? 0;
            sample.RoundTripMaxMs = SqliteCommandHelper.ReadNullableLong(reader["round_trip_max_ms"]);
            sample.ReconnectCount = SqliteCommandHelper.ReadNullableInt(reader["reconnect_count"]);
            sample.LastReconnectUtc = SqliteCommandHelper.ReadNullableDate(reader["last_reconnect_utc"]);
            sample.CreatedUtc = SqliteCommandHelper.ReadDate(reader["created_utc"]);
            return sample;
        }

        #endregion
    }
}
