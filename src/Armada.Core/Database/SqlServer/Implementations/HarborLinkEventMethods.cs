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
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQL Server implementation of Harbor link event persistence.
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
        /// <param name="driver">SQL Server database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public HarborLinkEventMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, linkEvent), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return linkEvent;
        }

        /// <inheritdoc />
        public async Task<List<HarborLinkEvent>> EnumerateAsync(string harborId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            return await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_link_events WHERE harbor_id = @harbor_id AND occurred_utc >= @from_utc AND occurred_utc < @to_utc ORDER BY occurred_utc ASC, id ASC;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@harbor_id", harborId);
                    SqlServerCommandHelper.AddDate(cmd, "@from_utc", fromUtc);
                    SqlServerCommandHelper.AddDate(cmd, "@to_utc", toUtc);
                }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<HarborLinkEvent?> ReadLatestBeforeAsync(string harborId, DateTime beforeUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            List<HarborLinkEvent> rows = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_link_events WHERE harbor_id = @harbor_id AND occurred_utc < @before_utc ORDER BY occurred_utc DESC, id DESC" + SqlServerCommandHelper.Page(0, 1) + ";",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@harbor_id", harborId);
                    SqlServerCommandHelper.AddDate(cmd, "@before_utc", beforeUtc);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<int> DeleteBeforeAsync(string harborId, DateTime beforeUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            int deleted = 0;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                deleted = await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM harbor_link_events WHERE harbor_id = @harbor_id AND occurred_utc < @before_utc;",
                    cmd =>
                    {
                        SqlServerCommandHelper.Add(cmd, "@harbor_id", harborId);
                        SqlServerCommandHelper.AddDate(cmd, "@before_utc", beforeUtc);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        /// <inheritdoc />
        public async Task<List<string>> EnumerateHarborIdsAsync(CancellationToken token = default)
        {
            return await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT DISTINCT harbor_id FROM harbor_link_events ORDER BY harbor_id ASC;",
                null, reader => reader["harbor_id"]?.ToString() ?? String.Empty, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> DeleteByHarborAsync(string harborId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            int deleted = 0;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                deleted = await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM harbor_link_events WHERE harbor_id = @harbor_id;",
                    cmd => SqlServerCommandHelper.Add(cmd, "@harbor_id", harborId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqlCommand cmd, HarborLinkEvent linkEvent)
        {
            SqlServerCommandHelper.Add(cmd, "@id", linkEvent.Id);
            SqlServerCommandHelper.Add(cmd, "@harbor_id", linkEvent.HarborId);
            SqlServerCommandHelper.Add(cmd, "@event_type", linkEvent.EventType.ToString());
            SqlServerCommandHelper.AddDate(cmd, "@occurred_utc", linkEvent.OccurredUtc);
            SqlServerCommandHelper.Add(cmd, "@detail", linkEvent.Detail);
        }

        private static HarborLinkEvent FromReader(SqlDataReader reader)
        {
            HarborLinkEvent linkEvent = new HarborLinkEvent();
            linkEvent.Id = reader["id"].ToString()!;
            linkEvent.HarborId = reader["harbor_id"]?.ToString() ?? String.Empty;
            linkEvent.EventType = SqlServerCommandHelper.ReadEnum(reader["event_type"], HarborLinkEventTypeEnum.Connected);
            linkEvent.OccurredUtc = SqlServerCommandHelper.ReadDate(reader["occurred_utc"]);
            linkEvent.Detail = SqlServerCommandHelper.ReadString(reader["detail"]);
            return linkEvent;
        }

        #endregion
    }
}
