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
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of Harbor link event persistence.
    /// </summary>
    public class HarborLinkEventMethods : IHarborLinkEventMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO harbor_link_events
            (id, harbor_id, event_type, occurred_utc, detail)
            VALUES
            (@id, @harbor_id, @event_type, @occurred_utc, @detail);";

        private readonly string _ConnectionString;
        private readonly SemaphoreSlim? _WriteLock;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when connectionString is null.</exception>
        public HarborLinkEventMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<HarborLinkEvent> CreateAsync(HarborLinkEvent linkEvent, CancellationToken token = default)
        {
            if (linkEvent == null) throw new ArgumentNullException(nameof(linkEvent));
            if (String.IsNullOrEmpty(linkEvent.HarborId)) throw new ArgumentException("HarborId is required.", nameof(linkEvent));
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, linkEvent), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return linkEvent;
        }

        /// <inheritdoc />
        public async Task<List<HarborLinkEvent>> EnumerateAsync(string harborId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_link_events WHERE harbor_id = @harbor_id AND occurred_utc >= @from_utc AND occurred_utc < @to_utc ORDER BY occurred_utc ASC, id ASC;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@harbor_id", harborId);
                    MysqlCommandHelper.AddDate(cmd, "@from_utc", fromUtc);
                    MysqlCommandHelper.AddDate(cmd, "@to_utc", toUtc);
                }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<HarborLinkEvent?> ReadLatestBeforeAsync(string harborId, DateTime beforeUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            List<HarborLinkEvent> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_link_events WHERE harbor_id = @harbor_id AND occurred_utc < @before_utc ORDER BY occurred_utc DESC, id DESC" + MysqlCommandHelper.Page(0, 1) + ";",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@harbor_id", harborId);
                    MysqlCommandHelper.AddDate(cmd, "@before_utc", beforeUtc);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<int> DeleteBeforeAsync(string harborId, DateTime beforeUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            int deleted = 0;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM harbor_link_events WHERE harbor_id = @harbor_id AND occurred_utc < @before_utc;",
                    cmd =>
                    {
                        MysqlCommandHelper.Add(cmd, "@harbor_id", harborId);
                        MysqlCommandHelper.AddDate(cmd, "@before_utc", beforeUtc);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        /// <inheritdoc />
        public async Task<List<string>> EnumerateHarborIdsAsync(CancellationToken token = default)
        {
            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT DISTINCT harbor_id FROM harbor_link_events ORDER BY harbor_id ASC;",
                null, reader => reader["harbor_id"]?.ToString() ?? String.Empty, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> DeleteByHarborAsync(string harborId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            int deleted = 0;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM harbor_link_events WHERE harbor_id = @harbor_id;",
                    cmd => MysqlCommandHelper.Add(cmd, "@harbor_id", harborId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void Bind(MySqlCommand cmd, HarborLinkEvent linkEvent)
        {
            MysqlCommandHelper.Add(cmd, "@id", linkEvent.Id);
            MysqlCommandHelper.Add(cmd, "@harbor_id", linkEvent.HarborId);
            MysqlCommandHelper.Add(cmd, "@event_type", linkEvent.EventType.ToString());
            MysqlCommandHelper.AddDate(cmd, "@occurred_utc", linkEvent.OccurredUtc);
            MysqlCommandHelper.Add(cmd, "@detail", linkEvent.Detail);
        }

        private static HarborLinkEvent FromReader(MySqlDataReader reader)
        {
            HarborLinkEvent linkEvent = new HarborLinkEvent();
            linkEvent.Id = reader["id"].ToString()!;
            linkEvent.HarborId = reader["harbor_id"]?.ToString() ?? String.Empty;
            linkEvent.EventType = MysqlCommandHelper.ReadEnum(reader["event_type"], HarborLinkEventTypeEnum.Connected);
            linkEvent.OccurredUtc = MysqlCommandHelper.ReadDate(reader["occurred_utc"]);
            linkEvent.Detail = MysqlCommandHelper.ReadString(reader["detail"]);
            return linkEvent;
        }

        #endregion
    }
}
