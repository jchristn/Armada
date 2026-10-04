namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
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
    /// SQL Server implementation of Ask Armada tracked-work persistence.
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
        /// <param name="driver">SQL Server database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public AskTrackedWorkMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
                await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
                {
                    object? present = await SqlServerCommandHelper.ScalarAsync(conn, tx,
                        "SELECT id FROM ask_tracked_work WHERE thread_id = @thread_id AND entity_type = @entity_type AND entity_id = @entity_id;",
                        cmd => BindKey(cmd, work.ThreadId, work.EntityType, work.EntityId), token).ConfigureAwait(false);
                    if (present != null) return;
                    await SqlServerCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, work), token).ConfigureAwait(false);
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

            List<AskTrackedWork> rows = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_tracked_work WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@id", id);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<AskTrackedWork> UpdateAsync(AskTrackedWork work, CancellationToken token = default)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (String.IsNullOrEmpty(work.TenantId)) throw new ArgumentException("TenantId is required.", nameof(work));

            work.LastUpdateUtc = DateTime.UtcNow;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE ask_tracked_work SET title = @title, status = @status, state = @state, snapshot_hash = @snapshot_hash, last_change_utc = @last_change_utc, completed_utc = @completed_utc, last_update_utc = @last_update_utc WHERE tenant_id = @tenant_id AND id = @id;",
                    cmd =>
                    {
                        SqlServerCommandHelper.Add(cmd, "@title", work.Title);
                        SqlServerCommandHelper.Add(cmd, "@status", work.Status);
                        SqlServerCommandHelper.Add(cmd, "@state", work.State.ToString());
                        SqlServerCommandHelper.Add(cmd, "@snapshot_hash", work.SnapshotHash);
                        SqlServerCommandHelper.AddDate(cmd, "@last_change_utc", work.LastChangeUtc);
                        SqlServerCommandHelper.AddDate(cmd, "@completed_utc", work.CompletedUtc);
                        SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", work.LastUpdateUtc);
                        SqlServerCommandHelper.Add(cmd, "@tenant_id", work.TenantId);
                        SqlServerCommandHelper.Add(cmd, "@id", work.Id);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return work;
        }

        /// <inheritdoc />
        public async Task<List<AskTrackedWork>> EnumerateByThreadAsync(string tenantId, string threadId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(threadId)) throw new ArgumentNullException(nameof(threadId));

            return await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_tracked_work WHERE tenant_id = @tenant_id AND thread_id = @thread_id ORDER BY created_utc DESC, id DESC;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@thread_id", threadId);
                }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<AskTrackedWork>> EnumerateActiveAsync(CancellationToken token = default)
        {
            return await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_tracked_work WHERE state = @state ORDER BY created_utc ASC, id ASC;",
                cmd => SqlServerCommandHelper.Add(cmd, "@state", AskTrackedWorkStateEnum.Active.ToString()),
                FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<AskTrackedWork>> EnumerateActiveByEntityAsync(AskTrackedEntityTypeEnum entityType, string entityId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entityId)) throw new ArgumentNullException(nameof(entityId));

            return await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_tracked_work WHERE entity_type = @entity_type AND entity_id = @entity_id AND state = @state ORDER BY created_utc ASC, id ASC;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@entity_type", entityType.ToString());
                    SqlServerCommandHelper.Add(cmd, "@entity_id", entityId);
                    SqlServerCommandHelper.Add(cmd, "@state", AskTrackedWorkStateEnum.Active.ToString());
                }, FromReader, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<AskTrackedWork?> ReadByKeyAsync(string threadId, AskTrackedEntityTypeEnum entityType, string entityId, CancellationToken token)
        {
            List<AskTrackedWork> rows = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_tracked_work WHERE thread_id = @thread_id AND entity_type = @entity_type AND entity_id = @entity_id;",
                cmd => BindKey(cmd, threadId, entityType, entityId), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        private static void BindKey(SqlCommand cmd, string threadId, AskTrackedEntityTypeEnum entityType, string entityId)
        {
            SqlServerCommandHelper.Add(cmd, "@thread_id", threadId);
            SqlServerCommandHelper.Add(cmd, "@entity_type", entityType.ToString());
            SqlServerCommandHelper.Add(cmd, "@entity_id", entityId);
        }

        private static void Bind(SqlCommand cmd, AskTrackedWork work)
        {
            SqlServerCommandHelper.Add(cmd, "@id", work.Id);
            SqlServerCommandHelper.Add(cmd, "@tenant_id", work.TenantId);
            SqlServerCommandHelper.Add(cmd, "@user_id", work.UserId);
            SqlServerCommandHelper.Add(cmd, "@thread_id", work.ThreadId);
            SqlServerCommandHelper.Add(cmd, "@entity_type", work.EntityType.ToString());
            SqlServerCommandHelper.Add(cmd, "@entity_id", work.EntityId);
            SqlServerCommandHelper.Add(cmd, "@title", work.Title);
            SqlServerCommandHelper.Add(cmd, "@status", work.Status);
            SqlServerCommandHelper.Add(cmd, "@state", work.State.ToString());
            SqlServerCommandHelper.Add(cmd, "@snapshot_hash", work.SnapshotHash);
            SqlServerCommandHelper.AddDate(cmd, "@last_change_utc", work.LastChangeUtc);
            SqlServerCommandHelper.AddDate(cmd, "@completed_utc", work.CompletedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@created_utc", work.CreatedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", work.LastUpdateUtc);
        }

        private static AskTrackedWork FromReader(SqlDataReader reader)
        {
            AskTrackedWork work = new AskTrackedWork();
            work.Id = reader["id"].ToString()!;
            work.TenantId = SqlServerCommandHelper.ReadString(reader["tenant_id"]);
            work.UserId = SqlServerCommandHelper.ReadString(reader["user_id"]);
            work.ThreadId = reader["thread_id"].ToString()!;
            work.EntityType = SqlServerCommandHelper.ReadEnum(reader["entity_type"], AskTrackedEntityTypeEnum.Voyage);
            work.EntityId = reader["entity_id"].ToString()!;
            work.Title = reader["title"]?.ToString() ?? String.Empty;
            work.Status = SqlServerCommandHelper.ReadString(reader["status"]);
            work.State = SqlServerCommandHelper.ReadEnum(reader["state"], AskTrackedWorkStateEnum.Active);
            work.SnapshotHash = SqlServerCommandHelper.ReadString(reader["snapshot_hash"]);
            work.LastChangeUtc = SqlServerCommandHelper.ReadNullableDate(reader["last_change_utc"]);
            work.CompletedUtc = SqlServerCommandHelper.ReadNullableDate(reader["completed_utc"]);
            work.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            work.LastUpdateUtc = SqlServerCommandHelper.ReadDate(reader["last_update_utc"]);
            return work;
        }

        #endregion
    }
}
