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
    /// SQL Server implementation of Ask Armada message persistence. Appends serialize per thread by updating the thread
    /// row first inside the transaction, so the next sequence number is computed while holding that row's lock.
    /// </summary>
    public class AskMessageMethods : IAskMessageMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO ask_messages
            (id, tenant_id, user_id, thread_id, sequence, role, kind, content_text, thinking_text, proposal_id, tracked_work_id, captain_id, duration_ms, created_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @user_id, @thread_id, @sequence, @role, @kind, @content_text, @thinking_text, @proposal_id, @tracked_work_id, @captain_id, @duration_ms, @created_utc, @last_update_utc);";

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
        public AskMessageMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<AskMessage> CreateAsync(AskMessage message, bool countsAsUnread, CancellationToken token = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (String.IsNullOrEmpty(message.TenantId)) throw new ArgumentException("TenantId is required.", nameof(message));
            if (String.IsNullOrEmpty(message.ThreadId)) throw new ArgumentException("ThreadId is required.", nameof(message));

            DateTime now = DateTime.UtcNow;
            message.CreatedUtc = now;
            message.LastUpdateUtc = now;

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                int touched = await SqlServerCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE ask_threads SET message_count = message_count + 1, unread_count = unread_count + @unread, last_message_utc = @now, last_update_utc = @now WHERE tenant_id = @tenant_id AND id = @thread_id;",
                    cmd =>
                    {
                        SqlServerCommandHelper.Add(cmd, "@unread", countsAsUnread ? 1 : 0);
                        SqlServerCommandHelper.AddDate(cmd, "@now", now);
                        SqlServerCommandHelper.Add(cmd, "@tenant_id", message.TenantId);
                        SqlServerCommandHelper.Add(cmd, "@thread_id", message.ThreadId);
                    }, token).ConfigureAwait(false);
                if (touched == 0) throw new KeyNotFoundException("Ask thread " + message.ThreadId + " was not found.");

                object? next = await SqlServerCommandHelper.ScalarAsync(conn, tx,
                    "SELECT COALESCE(MAX(sequence), 0) + 1 FROM ask_messages WHERE thread_id = @thread_id;",
                    cmd => SqlServerCommandHelper.Add(cmd, "@thread_id", message.ThreadId), token).ConfigureAwait(false);
                message.Sequence = next == null ? 1 : Convert.ToInt32(next);

                await SqlServerCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, message), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return message;
        }

        /// <inheritdoc />
        public async Task<AskMessage?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            List<AskMessage> rows = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_messages WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@id", id);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<AskMessage> UpdateAsync(AskMessage message, CancellationToken token = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (String.IsNullOrEmpty(message.TenantId)) throw new ArgumentException("TenantId is required.", nameof(message));

            message.LastUpdateUtc = DateTime.UtcNow;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE ask_messages SET kind = @kind, content_text = @content_text, thinking_text = @thinking_text, proposal_id = @proposal_id, tracked_work_id = @tracked_work_id, captain_id = @captain_id, duration_ms = @duration_ms, last_update_utc = @last_update_utc WHERE tenant_id = @tenant_id AND id = @id;",
                    cmd =>
                    {
                        SqlServerCommandHelper.Add(cmd, "@kind", message.Kind.ToString());
                        SqlServerCommandHelper.Add(cmd, "@content_text", message.ContentText);
                        SqlServerCommandHelper.Add(cmd, "@thinking_text", message.ThinkingText);
                        SqlServerCommandHelper.Add(cmd, "@proposal_id", message.ProposalId);
                        SqlServerCommandHelper.Add(cmd, "@tracked_work_id", message.TrackedWorkId);
                        SqlServerCommandHelper.Add(cmd, "@captain_id", message.CaptainId);
                        SqlServerCommandHelper.Add(cmd, "@duration_ms", message.DurationMs);
                        SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", message.LastUpdateUtc);
                        SqlServerCommandHelper.Add(cmd, "@tenant_id", message.TenantId);
                        SqlServerCommandHelper.Add(cmd, "@id", message.Id);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return message;
        }

        /// <inheritdoc />
        public async Task<AskMessagePage> EnumerateAsync(string tenantId, string threadId, int? beforeSequence, int pageSize, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(threadId)) throw new ArgumentNullException(nameof(threadId));
            int size = pageSize < 1 ? 1 : (pageSize > 500 ? 500 : pageSize);

            string sql = "SELECT * FROM ask_messages WHERE tenant_id = @tenant_id AND thread_id = @thread_id"
                + (beforeSequence.HasValue ? " AND sequence < @before" : String.Empty)
                + " ORDER BY sequence DESC" + SqlServerCommandHelper.Page(0, size + 1) + ";";

            List<AskMessage> rows = await SqlServerCommandHelper.QueryAsync(_ConnectionString, sql,
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@thread_id", threadId);
                    if (beforeSequence.HasValue) SqlServerCommandHelper.Add(cmd, "@before", beforeSequence.Value);
                }, FromReader, token).ConfigureAwait(false);

            AskMessagePage page = new AskMessagePage();
            page.HasMore = rows.Count > size;
            page.Messages = rows.Take(size).OrderBy(m => m.Sequence).ToList();
            return page;
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqlCommand cmd, AskMessage message)
        {
            SqlServerCommandHelper.Add(cmd, "@id", message.Id);
            SqlServerCommandHelper.Add(cmd, "@tenant_id", message.TenantId);
            SqlServerCommandHelper.Add(cmd, "@user_id", message.UserId);
            SqlServerCommandHelper.Add(cmd, "@thread_id", message.ThreadId);
            SqlServerCommandHelper.Add(cmd, "@sequence", message.Sequence);
            SqlServerCommandHelper.Add(cmd, "@role", message.Role.ToString());
            SqlServerCommandHelper.Add(cmd, "@kind", message.Kind.ToString());
            SqlServerCommandHelper.Add(cmd, "@content_text", message.ContentText);
            SqlServerCommandHelper.Add(cmd, "@thinking_text", message.ThinkingText);
            SqlServerCommandHelper.Add(cmd, "@proposal_id", message.ProposalId);
            SqlServerCommandHelper.Add(cmd, "@tracked_work_id", message.TrackedWorkId);
            SqlServerCommandHelper.Add(cmd, "@captain_id", message.CaptainId);
            SqlServerCommandHelper.Add(cmd, "@duration_ms", message.DurationMs);
            SqlServerCommandHelper.AddDate(cmd, "@created_utc", message.CreatedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", message.LastUpdateUtc);
        }

        private static AskMessage FromReader(SqlDataReader reader)
        {
            AskMessage message = new AskMessage();
            message.Id = reader["id"].ToString()!;
            message.TenantId = SqlServerCommandHelper.ReadString(reader["tenant_id"]);
            message.UserId = SqlServerCommandHelper.ReadString(reader["user_id"]);
            message.ThreadId = reader["thread_id"].ToString()!;
            message.Sequence = SqlServerCommandHelper.ReadInt(reader["sequence"], 0);
            message.Role = SqlServerCommandHelper.ReadEnum(reader["role"], AskMessageRoleEnum.User);
            message.Kind = SqlServerCommandHelper.ReadEnum(reader["kind"], AskMessageKindEnum.Text);
            message.ContentText = reader["content_text"]?.ToString() ?? String.Empty;
            message.ThinkingText = SqlServerCommandHelper.ReadString(reader["thinking_text"]);
            message.ProposalId = SqlServerCommandHelper.ReadString(reader["proposal_id"]);
            message.TrackedWorkId = SqlServerCommandHelper.ReadString(reader["tracked_work_id"]);
            message.CaptainId = SqlServerCommandHelper.ReadString(reader["captain_id"]);
            message.DurationMs = SqlServerCommandHelper.ReadNullableLong(reader["duration_ms"]);
            message.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            message.LastUpdateUtc = SqlServerCommandHelper.ReadDate(reader["last_update_utc"]);
            return message;
        }

        #endregion
    }
}
