namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of Ask Armada tracked-work persistence.
    /// </summary>
    public class AskTrackedWorkMethods : IAskTrackedWorkMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO ask_tracked_work
            (id, tenant_id, user_id, thread_id, entity_type, entity_id, title, status, state, snapshot_hash, last_change_utc, completed_utc, created_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @user_id, @thread_id, @entity_type, @entity_id, @title, @status, @state, @snapshot_hash, @last_change_utc, @completed_utc, @created_utc, @last_update_utc);";

        private readonly string _ConnectionString;
        private readonly SemaphoreSlim? _WriteLock;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when connectionString is null.</exception>
        public AskTrackedWorkMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
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
                await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
                {
                    object? present = await MysqlCommandHelper.ScalarAsync(conn, tx,
                        "SELECT id FROM ask_tracked_work WHERE thread_id = @thread_id AND entity_type = @entity_type AND entity_id = @entity_id;",
                        cmd => BindKey(cmd, work.ThreadId, work.EntityType, work.EntityId), token).ConfigureAwait(false);
                    if (present != null) return;
                    await MysqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, work), token).ConfigureAwait(false);
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

            List<AskTrackedWork> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_tracked_work WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@id", id);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<AskTrackedWork> UpdateAsync(AskTrackedWork work, CancellationToken token = default)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (String.IsNullOrEmpty(work.TenantId)) throw new ArgumentException("TenantId is required.", nameof(work));

            work.LastUpdateUtc = DateTime.UtcNow;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE ask_tracked_work SET title = @title, status = @status, state = @state, snapshot_hash = @snapshot_hash, last_change_utc = @last_change_utc, completed_utc = @completed_utc, last_update_utc = @last_update_utc WHERE tenant_id = @tenant_id AND id = @id;",
                    cmd =>
                    {
                        MysqlCommandHelper.Add(cmd, "@title", work.Title);
                        MysqlCommandHelper.Add(cmd, "@status", work.Status);
                        MysqlCommandHelper.Add(cmd, "@state", work.State.ToString());
                        MysqlCommandHelper.Add(cmd, "@snapshot_hash", work.SnapshotHash);
                        MysqlCommandHelper.AddDate(cmd, "@last_change_utc", work.LastChangeUtc);
                        MysqlCommandHelper.AddDate(cmd, "@completed_utc", work.CompletedUtc);
                        MysqlCommandHelper.AddDate(cmd, "@last_update_utc", work.LastUpdateUtc);
                        MysqlCommandHelper.Add(cmd, "@tenant_id", work.TenantId);
                        MysqlCommandHelper.Add(cmd, "@id", work.Id);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return work;
        }

        /// <inheritdoc />
        public async Task<List<AskTrackedWork>> EnumerateByThreadAsync(string tenantId, string threadId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(threadId)) throw new ArgumentNullException(nameof(threadId));

            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_tracked_work WHERE tenant_id = @tenant_id AND thread_id = @thread_id ORDER BY created_utc DESC, id DESC;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@thread_id", threadId);
                }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<AskTrackedWork>> EnumerateActiveAsync(CancellationToken token = default)
        {
            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_tracked_work WHERE state = @state ORDER BY created_utc ASC, id ASC;",
                cmd => MysqlCommandHelper.Add(cmd, "@state", AskTrackedWorkStateEnum.Active.ToString()),
                FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<AskTrackedWork>> EnumerateActiveByEntityAsync(AskTrackedEntityTypeEnum entityType, string entityId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entityId)) throw new ArgumentNullException(nameof(entityId));

            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_tracked_work WHERE entity_type = @entity_type AND entity_id = @entity_id AND state = @state ORDER BY created_utc ASC, id ASC;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@entity_type", entityType.ToString());
                    MysqlCommandHelper.Add(cmd, "@entity_id", entityId);
                    MysqlCommandHelper.Add(cmd, "@state", AskTrackedWorkStateEnum.Active.ToString());
                }, FromReader, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<AskTrackedWork?> ReadByKeyAsync(string threadId, AskTrackedEntityTypeEnum entityType, string entityId, CancellationToken token)
        {
            List<AskTrackedWork> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_tracked_work WHERE thread_id = @thread_id AND entity_type = @entity_type AND entity_id = @entity_id;",
                cmd => BindKey(cmd, threadId, entityType, entityId), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        private static void BindKey(MySqlCommand cmd, string threadId, AskTrackedEntityTypeEnum entityType, string entityId)
        {
            MysqlCommandHelper.Add(cmd, "@thread_id", threadId);
            MysqlCommandHelper.Add(cmd, "@entity_type", entityType.ToString());
            MysqlCommandHelper.Add(cmd, "@entity_id", entityId);
        }

        private static void Bind(MySqlCommand cmd, AskTrackedWork work)
        {
            MysqlCommandHelper.Add(cmd, "@id", work.Id);
            MysqlCommandHelper.Add(cmd, "@tenant_id", work.TenantId);
            MysqlCommandHelper.Add(cmd, "@user_id", work.UserId);
            MysqlCommandHelper.Add(cmd, "@thread_id", work.ThreadId);
            MysqlCommandHelper.Add(cmd, "@entity_type", work.EntityType.ToString());
            MysqlCommandHelper.Add(cmd, "@entity_id", work.EntityId);
            MysqlCommandHelper.Add(cmd, "@title", work.Title);
            MysqlCommandHelper.Add(cmd, "@status", work.Status);
            MysqlCommandHelper.Add(cmd, "@state", work.State.ToString());
            MysqlCommandHelper.Add(cmd, "@snapshot_hash", work.SnapshotHash);
            MysqlCommandHelper.AddDate(cmd, "@last_change_utc", work.LastChangeUtc);
            MysqlCommandHelper.AddDate(cmd, "@completed_utc", work.CompletedUtc);
            MysqlCommandHelper.AddDate(cmd, "@created_utc", work.CreatedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", work.LastUpdateUtc);
        }

        private static AskTrackedWork FromReader(MySqlDataReader reader)
        {
            AskTrackedWork work = new AskTrackedWork();
            work.Id = reader["id"].ToString()!;
            work.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            work.UserId = MysqlCommandHelper.ReadString(reader["user_id"]);
            work.ThreadId = reader["thread_id"].ToString()!;
            work.EntityType = MysqlCommandHelper.ReadEnum(reader["entity_type"], AskTrackedEntityTypeEnum.Voyage);
            work.EntityId = reader["entity_id"].ToString()!;
            work.Title = reader["title"]?.ToString() ?? String.Empty;
            work.Status = MysqlCommandHelper.ReadString(reader["status"]);
            work.State = MysqlCommandHelper.ReadEnum(reader["state"], AskTrackedWorkStateEnum.Active);
            work.SnapshotHash = MysqlCommandHelper.ReadString(reader["snapshot_hash"]);
            work.LastChangeUtc = MysqlCommandHelper.ReadNullableDate(reader["last_change_utc"]);
            work.CompletedUtc = MysqlCommandHelper.ReadNullableDate(reader["completed_utc"]);
            work.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            work.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return work;
        }

        #endregion
    }
}
