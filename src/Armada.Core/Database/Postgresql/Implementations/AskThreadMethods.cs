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
    /// PostgreSQL implementation of Ask Armada thread persistence.
    /// </summary>
    public class AskThreadMethods : IAskThreadMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO ask_threads
            (id, tenant_id, user_id, title, captain_id, auto_approve, summary_text, summary_utc, pinned, archived, last_message_utc, message_count, unread_count, created_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @user_id, @title, @captain_id, @auto_approve, @summary_text, @summary_utc, @pinned, @archived, @last_message_utc, @message_count, @unread_count, @created_utc, @last_update_utc);";

        private static readonly string _Update = @"UPDATE ask_threads SET
            title = @title, captain_id = @captain_id, auto_approve = @auto_approve, summary_text = @summary_text, summary_utc = @summary_utc,
            pinned = @pinned, archived = @archived, last_update_utc = @last_update_utc
            WHERE tenant_id = @tenant_id AND id = @id;";

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
        public AskThreadMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<AskThread> CreateAsync(AskThread thread, CancellationToken token = default)
        {
            if (thread == null) throw new ArgumentNullException(nameof(thread));
            if (String.IsNullOrEmpty(thread.TenantId)) throw new ArgumentException("TenantId is required.", nameof(thread));
            if (String.IsNullOrEmpty(thread.UserId)) throw new ArgumentException("UserId is required.", nameof(thread));

            thread.LastUpdateUtc = DateTime.UtcNow;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, thread), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return thread;
        }

        /// <inheritdoc />
        public async Task<AskThread?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            List<AskThread> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_threads WHERE tenant_id = @tenant_id AND user_id = @user_id AND id = @id;",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@user_id", userId);
                    PostgresqlCommandHelper.Add(cmd, "@id", id);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<AskThread?> ReadByIdAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            List<AskThread> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_threads WHERE id = @id;",
                cmd => PostgresqlCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<List<AskThread>> EnumerateInactiveAsync(DateTime inactiveBeforeUtc, bool includeArchived, int maxResults, CancellationToken token = default)
        {
            if (maxResults < 1) maxResults = 1;
            if (maxResults > 1000) maxResults = 1000;

            return await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_threads WHERE pinned = @pinned" + (includeArchived ? "" : " AND archived = @archived") + " AND COALESCE(last_message_utc, created_utc) < @cutoff ORDER BY COALESCE(last_message_utc, created_utc) ASC LIMIT " + maxResults + ";",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@pinned", false);
                    if (!includeArchived) PostgresqlCommandHelper.Add(cmd, "@archived", false);
                    PostgresqlCommandHelper.AddDate(cmd, "@cutoff", inactiveBeforeUtc);
                }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<AskThread> UpdateAsync(AskThread thread, CancellationToken token = default)
        {
            if (thread == null) throw new ArgumentNullException(nameof(thread));
            if (String.IsNullOrEmpty(thread.TenantId)) throw new ArgumentException("TenantId is required.", nameof(thread));

            thread.LastUpdateUtc = DateTime.UtcNow;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Update, cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@title", thread.Title);
                    PostgresqlCommandHelper.Add(cmd, "@captain_id", thread.CaptainId);
                    PostgresqlCommandHelper.Add(cmd, "@auto_approve", thread.AutoApprove);
                    PostgresqlCommandHelper.Add(cmd, "@summary_text", thread.SummaryText);
                    PostgresqlCommandHelper.AddDate(cmd, "@summary_utc", thread.SummaryUtc);
                    PostgresqlCommandHelper.Add(cmd, "@pinned", thread.Pinned);
                    PostgresqlCommandHelper.Add(cmd, "@archived", thread.Archived);
                    PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", thread.LastUpdateUtc);
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", thread.TenantId);
                    PostgresqlCommandHelper.Add(cmd, "@id", thread.Id);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            AskThread? stored = await ReadByIdAsync(thread.Id, token).ConfigureAwait(false);
            return stored ?? thread;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<AskThread>> EnumerateAsync(string tenantId, string userId, AskThreadEnumerateRequest request, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (request == null) request = new AskThreadEnumerateRequest();

            List<string> conditions = new List<string> { "tenant_id = @tenant_id", "user_id = @user_id" };
            if (!request.IncludeArchived) conditions.Add("archived = @archived");
            string? search = String.IsNullOrWhiteSpace(request.Search) ? null : "%" + request.Search.Trim().ToLowerInvariant() + "%";
            if (search != null) conditions.Add("LOWER(title) LIKE @search");

            Action<NpgsqlCommand> bind = cmd =>
            {
                PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                PostgresqlCommandHelper.Add(cmd, "@user_id", userId);
                if (!request.IncludeArchived) PostgresqlCommandHelper.Add(cmd, "@archived", false);
                if (search != null) PostgresqlCommandHelper.Add(cmd, "@search", search);
            };

            string where = " WHERE " + String.Join(" AND ", conditions);
            int offset = (request.PageNumber - 1) * request.PageSize;

            using (NpgsqlConnection conn = new NpgsqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await PostgresqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM ask_threads" + where + ";", bind, token).ConfigureAwait(false);
                List<AskThread> rows = await PostgresqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM ask_threads" + where + " ORDER BY pinned DESC, COALESCE(last_message_utc, created_utc) DESC, id DESC" + PostgresqlCommandHelper.Page(offset, request.PageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);

                EnumerationResult<AskThread> result = new EnumerationResult<AskThread>();
                result.PageNumber = request.PageNumber;
                result.PageSize = request.PageSize;
                result.TotalRecords = total;
                result.TotalPages = total == 0 ? 0 : (int)((total + request.PageSize - 1) / request.PageSize);
                result.Objects = rows;
                return result;
            }
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            int deleted = 0;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                object? owner = await PostgresqlCommandHelper.ScalarAsync(conn, tx,
                    "SELECT id FROM ask_threads WHERE tenant_id = @tenant_id AND user_id = @user_id AND id = @id;",
                    cmd =>
                    {
                        PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                        PostgresqlCommandHelper.Add(cmd, "@user_id", userId);
                        PostgresqlCommandHelper.Add(cmd, "@id", id);
                    }, token).ConfigureAwait(false);
                if (owner == null) return;

                Action<NpgsqlCommand> bindThread = cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@thread_id", id);
                };
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM ask_message_tool_calls WHERE tenant_id = @tenant_id AND thread_id = @thread_id;", bindThread, token).ConfigureAwait(false);
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM ask_action_proposals WHERE tenant_id = @tenant_id AND thread_id = @thread_id;", bindThread, token).ConfigureAwait(false);
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM ask_tracked_work WHERE tenant_id = @tenant_id AND thread_id = @thread_id;", bindThread, token).ConfigureAwait(false);
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM ask_messages WHERE tenant_id = @tenant_id AND thread_id = @thread_id;", bindThread, token).ConfigureAwait(false);
                deleted = await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM ask_threads WHERE tenant_id = @tenant_id AND id = @thread_id;", bindThread, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted > 0;
        }

        /// <inheritdoc />
        public async Task<bool> MarkReadAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            int updated = 0;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                updated = await PostgresqlCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE ask_threads SET unread_count = 0 WHERE tenant_id = @tenant_id AND user_id = @user_id AND id = @id;",
                    cmd =>
                    {
                        PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                        PostgresqlCommandHelper.Add(cmd, "@user_id", userId);
                        PostgresqlCommandHelper.Add(cmd, "@id", id);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return updated > 0;
        }

        #endregion

        #region Private-Methods

        private static void Bind(NpgsqlCommand cmd, AskThread thread)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", thread.Id);
            PostgresqlCommandHelper.Add(cmd, "@tenant_id", thread.TenantId);
            PostgresqlCommandHelper.Add(cmd, "@user_id", thread.UserId);
            PostgresqlCommandHelper.Add(cmd, "@title", thread.Title);
            PostgresqlCommandHelper.Add(cmd, "@captain_id", thread.CaptainId);
            PostgresqlCommandHelper.Add(cmd, "@auto_approve", thread.AutoApprove);
            PostgresqlCommandHelper.Add(cmd, "@summary_text", thread.SummaryText);
            PostgresqlCommandHelper.AddDate(cmd, "@summary_utc", thread.SummaryUtc);
            PostgresqlCommandHelper.Add(cmd, "@pinned", thread.Pinned);
            PostgresqlCommandHelper.Add(cmd, "@archived", thread.Archived);
            PostgresqlCommandHelper.AddDate(cmd, "@last_message_utc", thread.LastMessageUtc);
            PostgresqlCommandHelper.Add(cmd, "@message_count", thread.MessageCount);
            PostgresqlCommandHelper.Add(cmd, "@unread_count", thread.UnreadCount);
            PostgresqlCommandHelper.AddDate(cmd, "@created_utc", thread.CreatedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", thread.LastUpdateUtc);
        }

        private static AskThread FromReader(NpgsqlDataReader reader)
        {
            AskThread thread = new AskThread();
            thread.Id = reader["id"].ToString()!;
            thread.TenantId = PostgresqlCommandHelper.ReadString(reader["tenant_id"]);
            thread.UserId = PostgresqlCommandHelper.ReadString(reader["user_id"]);
            thread.Title = reader["title"].ToString()!;
            thread.CaptainId = PostgresqlCommandHelper.ReadString(reader["captain_id"]);
            thread.AutoApprove = PostgresqlCommandHelper.ReadBool(reader["auto_approve"], false);
            thread.SummaryText = PostgresqlCommandHelper.ReadString(reader["summary_text"]);
            thread.SummaryUtc = PostgresqlCommandHelper.ReadNullableDate(reader["summary_utc"]);
            thread.Pinned = PostgresqlCommandHelper.ReadBool(reader["pinned"], false);
            thread.Archived = PostgresqlCommandHelper.ReadBool(reader["archived"], false);
            thread.LastMessageUtc = PostgresqlCommandHelper.ReadNullableDate(reader["last_message_utc"]);
            thread.MessageCount = PostgresqlCommandHelper.ReadInt(reader["message_count"], 0);
            thread.UnreadCount = PostgresqlCommandHelper.ReadInt(reader["unread_count"], 0);
            thread.CreatedUtc = PostgresqlCommandHelper.ReadDate(reader["created_utc"]);
            thread.LastUpdateUtc = PostgresqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return thread;
        }

        #endregion
    }
}
