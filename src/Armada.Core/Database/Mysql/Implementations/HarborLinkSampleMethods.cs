namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of Harbor link-health sample persistence.
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
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when connectionString is null.</exception>
        public HarborLinkSampleMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<HarborLinkSample> CreateAsync(HarborLinkSample sample, CancellationToken token = default)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            if (String.IsNullOrEmpty(sample.HarborId)) throw new ArgumentException("HarborId is required.", nameof(sample));
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, sample), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return sample;
        }

        /// <inheritdoc />
        public async Task<List<HarborLinkSample>> EnumerateAsync(string harborId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_link_samples WHERE harbor_id = @harbor_id AND bucket_start_utc >= @from_utc AND bucket_start_utc < @to_utc ORDER BY bucket_start_utc ASC, id ASC;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@harbor_id", harborId);
                    MysqlCommandHelper.AddDate(cmd, "@from_utc", fromUtc);
                    MysqlCommandHelper.AddDate(cmd, "@to_utc", toUtc);
                }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<HarborLinkSample?> ReadLatestAsync(string harborId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            List<HarborLinkSample> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_link_samples WHERE harbor_id = @harbor_id ORDER BY bucket_start_utc DESC, id DESC" + MysqlCommandHelper.Page(0, 1) + ";",
                cmd => MysqlCommandHelper.Add(cmd, "@harbor_id", harborId), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<int> DeleteBeforeAsync(DateTime cutoffUtc, CancellationToken token = default)
        {
            int deleted = 0;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM harbor_link_samples WHERE bucket_start_utc < @cutoff;",
                    cmd => MysqlCommandHelper.AddDate(cmd, "@cutoff", cutoffUtc), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        /// <inheritdoc />
        public async Task<int> DeleteByHarborAsync(string harborId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            int deleted = 0;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM harbor_link_samples WHERE harbor_id = @harbor_id;",
                    cmd => MysqlCommandHelper.Add(cmd, "@harbor_id", harborId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void Bind(MySqlCommand cmd, HarborLinkSample sample)
        {
            MysqlCommandHelper.Add(cmd, "@id", sample.Id);
            MysqlCommandHelper.Add(cmd, "@harbor_id", sample.HarborId);
            MysqlCommandHelper.AddDate(cmd, "@bucket_start_utc", sample.BucketStartUtc);
            MysqlCommandHelper.Add(cmd, "@heartbeat_count", sample.HeartbeatCount);
            MysqlCommandHelper.Add(cmd, "@round_trip_count", sample.RoundTripCount);
            MysqlCommandHelper.Add(cmd, "@round_trip_total_ms", sample.RoundTripTotalMs);
            MysqlCommandHelper.Add(cmd, "@round_trip_max_ms", sample.RoundTripMaxMs);
            MysqlCommandHelper.Add(cmd, "@reconnect_count", sample.ReconnectCount);
            MysqlCommandHelper.AddDate(cmd, "@last_reconnect_utc", sample.LastReconnectUtc);
            MysqlCommandHelper.AddDate(cmd, "@created_utc", sample.CreatedUtc);
        }

        private static HarborLinkSample FromReader(MySqlDataReader reader)
        {
            HarborLinkSample sample = new HarborLinkSample();
            sample.Id = reader["id"].ToString()!;
            sample.HarborId = reader["harbor_id"]?.ToString() ?? String.Empty;
            sample.BucketStartUtc = MysqlCommandHelper.ReadDate(reader["bucket_start_utc"]);
            sample.HeartbeatCount = MysqlCommandHelper.ReadInt(reader["heartbeat_count"], 0);
            sample.RoundTripCount = MysqlCommandHelper.ReadInt(reader["round_trip_count"], 0);
            sample.RoundTripTotalMs = MysqlCommandHelper.ReadNullableLong(reader["round_trip_total_ms"]) ?? 0;
            sample.RoundTripMaxMs = MysqlCommandHelper.ReadNullableLong(reader["round_trip_max_ms"]);
            sample.ReconnectCount = MysqlCommandHelper.ReadNullableInt(reader["reconnect_count"]);
            sample.LastReconnectUtc = MysqlCommandHelper.ReadNullableDate(reader["last_reconnect_utc"]);
            sample.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            return sample;
        }

        #endregion
    }
}
