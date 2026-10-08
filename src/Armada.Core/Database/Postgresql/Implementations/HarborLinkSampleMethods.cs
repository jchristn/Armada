namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// PostgreSQL implementation of Harbor link-health sample persistence.
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
        /// <param name="driver">PostgreSQL database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public HarborLinkSampleMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, sample), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return sample;
        }

        /// <inheritdoc />
        public async Task<List<HarborLinkSample>> EnumerateAsync(string harborId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            return await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_link_samples WHERE harbor_id = @harbor_id AND bucket_start_utc >= @from_utc AND bucket_start_utc < @to_utc ORDER BY bucket_start_utc ASC, id ASC;",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@harbor_id", harborId);
                    PostgresqlCommandHelper.AddDate(cmd, "@from_utc", fromUtc);
                    PostgresqlCommandHelper.AddDate(cmd, "@to_utc", toUtc);
                }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<HarborLinkSample?> ReadLatestAsync(string harborId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            List<HarborLinkSample> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_link_samples WHERE harbor_id = @harbor_id ORDER BY bucket_start_utc DESC, id DESC" + PostgresqlCommandHelper.Page(0, 1) + ";",
                cmd => PostgresqlCommandHelper.Add(cmd, "@harbor_id", harborId), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<int> DeleteBeforeAsync(DateTime cutoffUtc, CancellationToken token = default)
        {
            int deleted = 0;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                deleted = await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM harbor_link_samples WHERE bucket_start_utc < @cutoff;",
                    cmd => PostgresqlCommandHelper.AddDate(cmd, "@cutoff", cutoffUtc), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        /// <inheritdoc />
        public async Task<int> DeleteByHarborAsync(string harborId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            int deleted = 0;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                deleted = await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM harbor_link_samples WHERE harbor_id = @harbor_id;",
                    cmd => PostgresqlCommandHelper.Add(cmd, "@harbor_id", harborId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void Bind(NpgsqlCommand cmd, HarborLinkSample sample)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", sample.Id);
            PostgresqlCommandHelper.Add(cmd, "@harbor_id", sample.HarborId);
            PostgresqlCommandHelper.AddDate(cmd, "@bucket_start_utc", sample.BucketStartUtc);
            PostgresqlCommandHelper.Add(cmd, "@heartbeat_count", sample.HeartbeatCount);
            PostgresqlCommandHelper.Add(cmd, "@round_trip_count", sample.RoundTripCount);
            PostgresqlCommandHelper.Add(cmd, "@round_trip_total_ms", sample.RoundTripTotalMs);
            PostgresqlCommandHelper.Add(cmd, "@round_trip_max_ms", sample.RoundTripMaxMs);
            PostgresqlCommandHelper.Add(cmd, "@reconnect_count", sample.ReconnectCount);
            PostgresqlCommandHelper.AddDate(cmd, "@last_reconnect_utc", sample.LastReconnectUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@created_utc", sample.CreatedUtc);
        }

        private static HarborLinkSample FromReader(NpgsqlDataReader reader)
        {
            HarborLinkSample sample = new HarborLinkSample();
            sample.Id = reader["id"].ToString()!;
            sample.HarborId = reader["harbor_id"]?.ToString() ?? String.Empty;
            sample.BucketStartUtc = PostgresqlCommandHelper.ReadDate(reader["bucket_start_utc"]);
            sample.HeartbeatCount = PostgresqlCommandHelper.ReadInt(reader["heartbeat_count"], 0);
            sample.RoundTripCount = PostgresqlCommandHelper.ReadInt(reader["round_trip_count"], 0);
            sample.RoundTripTotalMs = PostgresqlCommandHelper.ReadNullableLong(reader["round_trip_total_ms"]) ?? 0;
            sample.RoundTripMaxMs = PostgresqlCommandHelper.ReadNullableLong(reader["round_trip_max_ms"]);
            sample.ReconnectCount = PostgresqlCommandHelper.ReadNullableInt(reader["reconnect_count"]);
            sample.LastReconnectUtc = PostgresqlCommandHelper.ReadNullableDate(reader["last_reconnect_utc"]);
            sample.CreatedUtc = PostgresqlCommandHelper.ReadDate(reader["created_utc"]);
            return sample;
        }

        #endregion
    }
}
