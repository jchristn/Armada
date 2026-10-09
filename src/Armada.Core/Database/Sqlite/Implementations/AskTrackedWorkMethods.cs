namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of Ask Armada tracked-work persistence.
    /// </summary>
    public class AskTrackedWorkMethods : IAskTrackedWorkMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO ask_tracked_work
            (id, tenant_id, user_id, thread_id, entity_type, entity_id, title, status, state, snapshot_hash, last_change_utc, completed_utc, created_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @user_id, @thread_id, @entity_type, @entity_id, @title, @status, @state, @snapshot_hash, @last_change_utc, @completed_utc, @created_utc, @last_update_utc);";

        private readonly string _ConnectionString;
        private readonly SqliteWriteGate _WriteGate;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQLite database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public AskTrackedWorkMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _ConnectionString = driver.ConnectionString;
            _WriteGate = driver.WriteGate;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<AskTrackedWork> CreateOrGetAsync(AskTrackedWork work, CancellationToken token = default)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (String.IsNullOrEmpty(work.TenantId)) throw new ArgumentException("TenantId is required.", nameof(work));
            if (String.IsNullOrEmpty(work.ThreadId)) throw new ArgumentException("ThreadId is required.", nameof(work));
            if (String.IsNullOrEmpty(work.EntityId)) throw new ArgumentException("EntityId is required.", nameof(work));

            AskTrackedWork? existing = await ReadByKeyAsync(work.ThreadId, work.EntityType, work.EntityId, token).ConfigureAwait(false);
            if (existing != null) return existing;

            work.LastUpdateUtc = DateTime.UtcNow;
            try
            {
                await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
                {
                    object? present = await SqliteCommandHelper.ScalarAsync(conn, tx,
                        "SELECT id FROM ask_tracked_work WHERE thread_id = @thread_id AND entity_type = @entity_type AND entity_id = @entity_id;",
                        cmd => BindKey(cmd, work.ThreadId, work.EntityType, work.EntityId), token).ConfigureAwait(false);
                    if (present != null) return;
                    await SqliteCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, work), token).ConfigureAwait(false);
                }, token).ConfigureAwait(false);
            }
            catch (DbException)
            {
                // A concurrent insert of the same (thread, entity) won the unique index; return that row below.
            }

            AskTrackedWork? stored = await ReadByKeyAsync(work.ThreadId, work.EntityType, work.EntityId, token).ConfigureAwait(false);
            if (stored == null) throw new InvalidOperationException("Tracked work for " + work.EntityType + " " + work.EntityId + " could not be stored.");
            return stored;
        }

        /// <inheritdoc />
        public async Task<AskTrackedWork?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            List<AskTrackedWork> rows = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_tracked_work WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqliteCommandHelper.Add(cmd, "@id", id);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<AskTrackedWork> UpdateAsync(AskTrackedWork work, CancellationToken token = default)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (String.IsNullOrEmpty(work.TenantId)) throw new ArgumentException("TenantId is required.", nameof(work));

            work.LastUpdateUtc = DateTime.UtcNow;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE ask_tracked_work SET title = @title, status = @status, state = @state, snapshot_hash = @snapshot_hash, last_change_utc = @last_change_utc, completed_utc = @completed_utc, last_update_utc = @last_update_utc WHERE tenant_id = @tenant_id AND id = @id;",
                    cmd =>
                    {
                        SqliteCommandHelper.Add(cmd, "@title", work.Title);
                        SqliteCommandHelper.Add(cmd, "@status", work.Status);
                        SqliteCommandHelper.Add(cmd, "@state", work.State.ToString());
                        SqliteCommandHelper.Add(cmd, "@snapshot_hash", work.SnapshotHash);
                        SqliteCommandHelper.AddDate(cmd, "@last_change_utc", work.LastChangeUtc);
                        SqliteCommandHelper.AddDate(cmd, "@completed_utc", work.CompletedUtc);
                        SqliteCommandHelper.AddDate(cmd, "@last_update_utc", work.LastUpdateUtc);
                        SqliteCommandHelper.Add(cmd, "@tenant_id", work.TenantId);
                        SqliteCommandHelper.Add(cmd, "@id", work.Id);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return work;
        }

        /// <inheritdoc />
        public async Task<List<AskTrackedWork>> EnumerateByThreadAsync(string tenantId, string threadId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(threadId)) throw new ArgumentNullException(nameof(threadId));

            return await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_tracked_work WHERE tenant_id = @tenant_id AND thread_id = @thread_id ORDER BY created_utc DESC, id DESC;",
                cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqliteCommandHelper.Add(cmd, "@thread_id", threadId);
                }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<AskTrackedWork>> EnumerateActiveAsync(CancellationToken token = default)
        {
            return await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_tracked_work WHERE state = @state ORDER BY created_utc ASC, id ASC;",
                cmd => SqliteCommandHelper.Add(cmd, "@state", AskTrackedWorkStateEnum.Active.ToString()),
                FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<AskTrackedWork>> EnumerateActiveByEntityAsync(AskTrackedEntityTypeEnum entityType, string entityId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entityId)) throw new ArgumentNullException(nameof(entityId));

            return await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_tracked_work WHERE entity_type = @entity_type AND entity_id = @entity_id AND state = @state ORDER BY created_utc ASC, id ASC;",
                cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@entity_type", entityType.ToString());
                    SqliteCommandHelper.Add(cmd, "@entity_id", entityId);
                    SqliteCommandHelper.Add(cmd, "@state", AskTrackedWorkStateEnum.Active.ToString());
                }, FromReader, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<AskTrackedWork?> ReadByKeyAsync(string threadId, AskTrackedEntityTypeEnum entityType, string entityId, CancellationToken token)
        {
            List<AskTrackedWork> rows = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_tracked_work WHERE thread_id = @thread_id AND entity_type = @entity_type AND entity_id = @entity_id;",
                cmd => BindKey(cmd, threadId, entityType, entityId), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        private static void BindKey(SqliteCommand cmd, string threadId, AskTrackedEntityTypeEnum entityType, string entityId)
        {
            SqliteCommandHelper.Add(cmd, "@thread_id", threadId);
            SqliteCommandHelper.Add(cmd, "@entity_type", entityType.ToString());
            SqliteCommandHelper.Add(cmd, "@entity_id", entityId);
        }

        private static void Bind(SqliteCommand cmd, AskTrackedWork work)
        {
            SqliteCommandHelper.Add(cmd, "@id", work.Id);
            SqliteCommandHelper.Add(cmd, "@tenant_id", work.TenantId);
            SqliteCommandHelper.Add(cmd, "@user_id", work.UserId);
            SqliteCommandHelper.Add(cmd, "@thread_id", work.ThreadId);
            SqliteCommandHelper.Add(cmd, "@entity_type", work.EntityType.ToString());
            SqliteCommandHelper.Add(cmd, "@entity_id", work.EntityId);
            SqliteCommandHelper.Add(cmd, "@title", work.Title);
            SqliteCommandHelper.Add(cmd, "@status", work.Status);
            SqliteCommandHelper.Add(cmd, "@state", work.State.ToString());
            SqliteCommandHelper.Add(cmd, "@snapshot_hash", work.SnapshotHash);
            SqliteCommandHelper.AddDate(cmd, "@last_change_utc", work.LastChangeUtc);
            SqliteCommandHelper.AddDate(cmd, "@completed_utc", work.CompletedUtc);
            SqliteCommandHelper.AddDate(cmd, "@created_utc", work.CreatedUtc);
            SqliteCommandHelper.AddDate(cmd, "@last_update_utc", work.LastUpdateUtc);
        }

        private static AskTrackedWork FromReader(SqliteDataReader reader)
        {
            AskTrackedWork work = new AskTrackedWork();
            work.Id = reader["id"].ToString()!;
            work.TenantId = SqliteCommandHelper.ReadString(reader["tenant_id"]);
            work.UserId = SqliteCommandHelper.ReadString(reader["user_id"]);
            work.ThreadId = reader["thread_id"].ToString()!;
            work.EntityType = SqliteCommandHelper.ReadEnum(reader["entity_type"], AskTrackedEntityTypeEnum.Voyage);
            work.EntityId = reader["entity_id"].ToString()!;
            work.Title = reader["title"]?.ToString() ?? String.Empty;
            work.Status = SqliteCommandHelper.ReadString(reader["status"]);
            work.State = SqliteCommandHelper.ReadEnum(reader["state"], AskTrackedWorkStateEnum.Active);
            work.SnapshotHash = SqliteCommandHelper.ReadString(reader["snapshot_hash"]);
            work.LastChangeUtc = SqliteCommandHelper.ReadNullableDate(reader["last_change_utc"]);
            work.CompletedUtc = SqliteCommandHelper.ReadNullableDate(reader["completed_utc"]);
            work.CreatedUtc = SqliteCommandHelper.ReadDate(reader["created_utc"]);
            work.LastUpdateUtc = SqliteCommandHelper.ReadDate(reader["last_update_utc"]);
            return work;
        }

        #endregion
    }
}
