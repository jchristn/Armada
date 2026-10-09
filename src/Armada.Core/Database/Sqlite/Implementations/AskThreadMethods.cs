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
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of Ask Armada thread persistence.
    /// </summary>
    public class AskThreadMethods : IAskThreadMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO ask_threads
            (id, tenant_id, user_id, title, captain_id, auto_approve, cli_permission_policy, summary_text, summary_utc, pinned, archived, last_message_utc, message_count, unread_count, created_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @user_id, @title, @captain_id, @auto_approve, @cli_permission_policy, @summary_text, @summary_utc, @pinned, @archived, @last_message_utc, @message_count, @unread_count, @created_utc, @last_update_utc);";

        private static readonly string _Update = @"UPDATE ask_threads SET
            title = @title, captain_id = @captain_id, auto_approve = @auto_approve, summary_text = @summary_text, summary_utc = @summary_utc,
            pinned = @pinned, archived = @archived, last_update_utc = @last_update_utc
            WHERE tenant_id = @tenant_id AND id = @id;";

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
        public AskThreadMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<AskThread> CreateAsync(AskThread thread, CancellationToken token = default)
        {
            if (thread == null) throw new ArgumentNullException(nameof(thread));
            if (String.IsNullOrEmpty(thread.TenantId)) throw new ArgumentException("TenantId is required.", nameof(thread));
            if (String.IsNullOrEmpty(thread.UserId)) throw new ArgumentException("UserId is required.", nameof(thread));

            thread.LastUpdateUtc = DateTime.UtcNow;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, thread), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return thread;
        }

        /// <inheritdoc />
        public async Task<AskThread?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            List<AskThread> rows = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_threads WHERE tenant_id = @tenant_id AND user_id = @user_id AND id = @id;",
                cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqliteCommandHelper.Add(cmd, "@user_id", userId);
                    SqliteCommandHelper.Add(cmd, "@id", id);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<AskThread?> ReadByIdAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            List<AskThread> rows = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_threads WHERE id = @id;",
                cmd => SqliteCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<List<AskThread>> EnumerateInactiveAsync(DateTime inactiveBeforeUtc, bool includeArchived, int maxResults, CancellationToken token = default)
        {
            if (maxResults < 1) maxResults = 1;
            if (maxResults > 1000) maxResults = 1000;

            return await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_threads WHERE pinned = @pinned" + (includeArchived ? "" : " AND archived = @archived") + " AND COALESCE(last_message_utc, created_utc) < @cutoff ORDER BY COALESCE(last_message_utc, created_utc) ASC LIMIT " + maxResults + ";",
                cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@pinned", false);
                    if (!includeArchived) SqliteCommandHelper.Add(cmd, "@archived", false);
                    SqliteCommandHelper.AddDate(cmd, "@cutoff", inactiveBeforeUtc);
                }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> UpdateCliPermissionPolicyAsync(string tenantId, string id, CliPermissionPolicyEnum? policy, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            int updated = 0;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                updated = await SqliteCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE ask_threads SET cli_permission_policy = @policy, last_update_utc = @now WHERE tenant_id = @tenant_id AND id = @id;",
                    cmd =>
                    {
                        SqliteCommandHelper.Add(cmd, "@policy", policy?.ToString());
                        SqliteCommandHelper.AddDate(cmd, "@now", DateTime.UtcNow);
                        SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                        SqliteCommandHelper.Add(cmd, "@id", id);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return updated > 0;
        }

        /// <inheritdoc />
        public async Task<AskThread> UpdateAsync(AskThread thread, CancellationToken token = default)
        {
            if (thread == null) throw new ArgumentNullException(nameof(thread));
            if (String.IsNullOrEmpty(thread.TenantId)) throw new ArgumentException("TenantId is required.", nameof(thread));

            thread.LastUpdateUtc = DateTime.UtcNow;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, _Update, cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@title", thread.Title);
                    SqliteCommandHelper.Add(cmd, "@captain_id", thread.CaptainId);
                    SqliteCommandHelper.Add(cmd, "@auto_approve", thread.AutoApprove);
                    SqliteCommandHelper.Add(cmd, "@summary_text", thread.SummaryText);
                    SqliteCommandHelper.AddDate(cmd, "@summary_utc", thread.SummaryUtc);
                    SqliteCommandHelper.Add(cmd, "@pinned", thread.Pinned);
                    SqliteCommandHelper.Add(cmd, "@archived", thread.Archived);
                    SqliteCommandHelper.AddDate(cmd, "@last_update_utc", thread.LastUpdateUtc);
                    SqliteCommandHelper.Add(cmd, "@tenant_id", thread.TenantId);
                    SqliteCommandHelper.Add(cmd, "@id", thread.Id);
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

            Action<SqliteCommand> bind = cmd =>
            {
                SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                SqliteCommandHelper.Add(cmd, "@user_id", userId);
                if (!request.IncludeArchived) SqliteCommandHelper.Add(cmd, "@archived", false);
                if (search != null) SqliteCommandHelper.Add(cmd, "@search", search);
            };

            string where = " WHERE " + String.Join(" AND ", conditions);
            int offset = (request.PageNumber - 1) * request.PageSize;

            using (SqliteConnection conn = new SqliteProviderConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqliteCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM ask_threads" + where + ";", bind, token).ConfigureAwait(false);
                List<AskThread> rows = await SqliteCommandHelper.QueryAsync(conn,
                    "SELECT * FROM ask_threads" + where + " ORDER BY pinned DESC, COALESCE(last_message_utc, created_utc) DESC, id DESC" + SqliteCommandHelper.Page(offset, request.PageSize) + ";",
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
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                object? owner = await SqliteCommandHelper.ScalarAsync(conn, tx,
                    "SELECT id FROM ask_threads WHERE tenant_id = @tenant_id AND user_id = @user_id AND id = @id;",
                    cmd =>
                    {
                        SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                        SqliteCommandHelper.Add(cmd, "@user_id", userId);
                        SqliteCommandHelper.Add(cmd, "@id", id);
                    }, token).ConfigureAwait(false);
                if (owner == null) return;

                Action<SqliteCommand> bindThread = cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqliteCommandHelper.Add(cmd, "@thread_id", id);
                };
                // The thread's CLI tool permission requests go with it (their cards are its messages).
                await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM cli_permission_requests WHERE tenant_id = @tenant_id AND thread_id = @thread_id;", bindThread, token).ConfigureAwait(false);
                await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM ask_message_tool_calls WHERE tenant_id = @tenant_id AND thread_id = @thread_id;", bindThread, token).ConfigureAwait(false);
                await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM ask_action_proposals WHERE tenant_id = @tenant_id AND thread_id = @thread_id;", bindThread, token).ConfigureAwait(false);
                await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM ask_tracked_work WHERE tenant_id = @tenant_id AND thread_id = @thread_id;", bindThread, token).ConfigureAwait(false);
                await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM ask_messages WHERE tenant_id = @tenant_id AND thread_id = @thread_id;", bindThread, token).ConfigureAwait(false);
                deleted = await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM ask_threads WHERE tenant_id = @tenant_id AND id = @thread_id;", bindThread, token).ConfigureAwait(false);
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
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                updated = await SqliteCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE ask_threads SET unread_count = 0 WHERE tenant_id = @tenant_id AND user_id = @user_id AND id = @id;",
                    cmd =>
                    {
                        SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                        SqliteCommandHelper.Add(cmd, "@user_id", userId);
                        SqliteCommandHelper.Add(cmd, "@id", id);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return updated > 0;
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqliteCommand cmd, AskThread thread)
        {
            SqliteCommandHelper.Add(cmd, "@id", thread.Id);
            SqliteCommandHelper.Add(cmd, "@tenant_id", thread.TenantId);
            SqliteCommandHelper.Add(cmd, "@user_id", thread.UserId);
            SqliteCommandHelper.Add(cmd, "@title", thread.Title);
            SqliteCommandHelper.Add(cmd, "@captain_id", thread.CaptainId);
            SqliteCommandHelper.Add(cmd, "@auto_approve", thread.AutoApprove);
            SqliteCommandHelper.Add(cmd, "@cli_permission_policy", thread.CliPermissionPolicy?.ToString());
            SqliteCommandHelper.Add(cmd, "@summary_text", thread.SummaryText);
            SqliteCommandHelper.AddDate(cmd, "@summary_utc", thread.SummaryUtc);
            SqliteCommandHelper.Add(cmd, "@pinned", thread.Pinned);
            SqliteCommandHelper.Add(cmd, "@archived", thread.Archived);
            SqliteCommandHelper.AddDate(cmd, "@last_message_utc", thread.LastMessageUtc);
            SqliteCommandHelper.Add(cmd, "@message_count", thread.MessageCount);
            SqliteCommandHelper.Add(cmd, "@unread_count", thread.UnreadCount);
            SqliteCommandHelper.AddDate(cmd, "@created_utc", thread.CreatedUtc);
            SqliteCommandHelper.AddDate(cmd, "@last_update_utc", thread.LastUpdateUtc);
        }

        private static AskThread FromReader(SqliteDataReader reader)
        {
            AskThread thread = new AskThread();
            thread.Id = reader["id"].ToString()!;
            thread.TenantId = SqliteCommandHelper.ReadString(reader["tenant_id"]);
            thread.UserId = SqliteCommandHelper.ReadString(reader["user_id"]);
            thread.Title = reader["title"].ToString()!;
            thread.CaptainId = SqliteCommandHelper.ReadString(reader["captain_id"]);
            thread.AutoApprove = SqliteCommandHelper.ReadBool(reader["auto_approve"], false);
            string? cliPolicy = SqliteCommandHelper.ReadString(reader["cli_permission_policy"]);
            thread.CliPermissionPolicy = cliPolicy != null && Enum.TryParse<CliPermissionPolicyEnum>(cliPolicy, true, out CliPermissionPolicyEnum parsedPolicy) ? parsedPolicy : (CliPermissionPolicyEnum?)null;
            thread.SummaryText = SqliteCommandHelper.ReadString(reader["summary_text"]);
            thread.SummaryUtc = SqliteCommandHelper.ReadNullableDate(reader["summary_utc"]);
            thread.Pinned = SqliteCommandHelper.ReadBool(reader["pinned"], false);
            thread.Archived = SqliteCommandHelper.ReadBool(reader["archived"], false);
            thread.LastMessageUtc = SqliteCommandHelper.ReadNullableDate(reader["last_message_utc"]);
            thread.MessageCount = SqliteCommandHelper.ReadInt(reader["message_count"], 0);
            thread.UnreadCount = SqliteCommandHelper.ReadInt(reader["unread_count"], 0);
            thread.CreatedUtc = SqliteCommandHelper.ReadDate(reader["created_utc"]);
            thread.LastUpdateUtc = SqliteCommandHelper.ReadDate(reader["last_update_utc"]);
            return thread;
        }

        #endregion
    }
}
