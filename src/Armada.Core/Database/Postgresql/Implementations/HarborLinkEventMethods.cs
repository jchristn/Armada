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
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// PostgreSQL implementation of Harbor link event persistence.
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
        /// <param name="driver">PostgreSQL database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public HarborLinkEventMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<HarborLinkEvent> CreateAsync(HarborLinkEvent linkEvent, CancellationToken token = default)
        {
            if (linkEvent == null) throw new ArgumentNullException(nameof(linkEvent));
            if (String.IsNullOrEmpty(linkEvent.HarborId)) throw new ArgumentException("HarborId is required.", nameof(linkEvent));
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, linkEvent), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return linkEvent;
        }

        /// <inheritdoc />
        public async Task<List<HarborLinkEvent>> EnumerateAsync(string harborId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            return await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_link_events WHERE harbor_id = @harbor_id AND occurred_utc >= @from_utc AND occurred_utc < @to_utc ORDER BY occurred_utc ASC, id ASC;",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@harbor_id", harborId);
                    PostgresqlCommandHelper.AddDate(cmd, "@from_utc", fromUtc);
                    PostgresqlCommandHelper.AddDate(cmd, "@to_utc", toUtc);
                }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<HarborLinkEvent?> ReadLatestBeforeAsync(string harborId, DateTime beforeUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            List<HarborLinkEvent> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_link_events WHERE harbor_id = @harbor_id AND occurred_utc < @before_utc ORDER BY occurred_utc DESC, id DESC" + PostgresqlCommandHelper.Page(0, 1) + ";",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@harbor_id", harborId);
                    PostgresqlCommandHelper.AddDate(cmd, "@before_utc", beforeUtc);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<int> DeleteBeforeAsync(string harborId, DateTime beforeUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            int deleted = 0;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                deleted = await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM harbor_link_events WHERE harbor_id = @harbor_id AND occurred_utc < @before_utc;",
                    cmd =>
                    {
                        PostgresqlCommandHelper.Add(cmd, "@harbor_id", harborId);
                        PostgresqlCommandHelper.AddDate(cmd, "@before_utc", beforeUtc);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        /// <inheritdoc />
        public async Task<List<string>> EnumerateHarborIdsAsync(CancellationToken token = default)
        {
            return await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT DISTINCT harbor_id FROM harbor_link_events ORDER BY harbor_id ASC;",
                null, reader => reader["harbor_id"]?.ToString() ?? String.Empty, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> DeleteByHarborAsync(string harborId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            int deleted = 0;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                deleted = await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM harbor_link_events WHERE harbor_id = @harbor_id;",
                    cmd => PostgresqlCommandHelper.Add(cmd, "@harbor_id", harborId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void Bind(NpgsqlCommand cmd, HarborLinkEvent linkEvent)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", linkEvent.Id);
            PostgresqlCommandHelper.Add(cmd, "@harbor_id", linkEvent.HarborId);
            PostgresqlCommandHelper.Add(cmd, "@event_type", linkEvent.EventType.ToString());
            PostgresqlCommandHelper.AddDate(cmd, "@occurred_utc", linkEvent.OccurredUtc);
            PostgresqlCommandHelper.Add(cmd, "@detail", linkEvent.Detail);
        }

        private static HarborLinkEvent FromReader(NpgsqlDataReader reader)
        {
            HarborLinkEvent linkEvent = new HarborLinkEvent();
            linkEvent.Id = reader["id"].ToString()!;
            linkEvent.HarborId = reader["harbor_id"]?.ToString() ?? String.Empty;
            linkEvent.EventType = PostgresqlCommandHelper.ReadEnum(reader["event_type"], HarborLinkEventTypeEnum.Connected);
            linkEvent.OccurredUtc = PostgresqlCommandHelper.ReadDate(reader["occurred_utc"]);
            linkEvent.Detail = PostgresqlCommandHelper.ReadString(reader["detail"]);
            return linkEvent;
        }

        #endregion
    }
}
