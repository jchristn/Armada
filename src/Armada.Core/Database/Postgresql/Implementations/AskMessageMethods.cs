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
    /// PostgreSQL implementation of Ask Armada message persistence. Appends serialize per thread by updating the thread
    /// row first inside the transaction, so the next sequence number is computed while holding that row's lock.
    /// </summary>
    public class AskMessageMethods : IAskMessageMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO ask_messages
            (id, tenant_id, user_id, thread_id, sequence, role, kind, content_text, thinking_text, proposal_id, tracked_work_id, captain_id, duration_ms, ttft_ms, first_text_ms, streaming_ms, tokens_per_second, input_tokens, output_tokens, cached_tokens, tokens_estimated, cost_usd, tool_call_count, tool_time_ms, created_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @user_id, @thread_id, @sequence, @role, @kind, @content_text, @thinking_text, @proposal_id, @tracked_work_id, @captain_id, @duration_ms, @ttft_ms, @first_text_ms, @streaming_ms, @tokens_per_second, @input_tokens, @output_tokens, @cached_tokens, @tokens_estimated, @cost_usd, @tool_call_count, @tool_time_ms, @created_utc, @last_update_utc);";

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
        public AskMessageMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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

            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                int touched = await PostgresqlCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE ask_threads SET message_count = message_count + 1, unread_count = unread_count + @unread, last_message_utc = @now, last_update_utc = @now WHERE tenant_id = @tenant_id AND id = @thread_id;",
                    cmd =>
                    {
                        PostgresqlCommandHelper.Add(cmd, "@unread", countsAsUnread ? 1 : 0);
                        PostgresqlCommandHelper.AddDate(cmd, "@now", now);
                        PostgresqlCommandHelper.Add(cmd, "@tenant_id", message.TenantId);
                        PostgresqlCommandHelper.Add(cmd, "@thread_id", message.ThreadId);
                    }, token).ConfigureAwait(false);
                if (touched == 0) throw new KeyNotFoundException("Ask thread " + message.ThreadId + " was not found.");

                object? next = await PostgresqlCommandHelper.ScalarAsync(conn, tx,
                    "SELECT COALESCE(MAX(sequence), 0) + 1 FROM ask_messages WHERE thread_id = @thread_id;",
                    cmd => PostgresqlCommandHelper.Add(cmd, "@thread_id", message.ThreadId), token).ConfigureAwait(false);
                message.Sequence = next == null ? 1 : Convert.ToInt32(next);

                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, message), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return message;
        }

        /// <inheritdoc />
        public async Task<AskMessage?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            List<AskMessage> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_messages WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@id", id);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<AskMessage> UpdateAsync(AskMessage message, CancellationToken token = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (String.IsNullOrEmpty(message.TenantId)) throw new ArgumentException("TenantId is required.", nameof(message));

            message.LastUpdateUtc = DateTime.UtcNow;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE ask_messages SET kind = @kind, content_text = @content_text, thinking_text = @thinking_text, proposal_id = @proposal_id, tracked_work_id = @tracked_work_id, captain_id = @captain_id, duration_ms = @duration_ms, ttft_ms = @ttft_ms, first_text_ms = @first_text_ms, streaming_ms = @streaming_ms, tokens_per_second = @tokens_per_second, input_tokens = @input_tokens, output_tokens = @output_tokens, cached_tokens = @cached_tokens, tokens_estimated = @tokens_estimated, cost_usd = @cost_usd, tool_call_count = @tool_call_count, tool_time_ms = @tool_time_ms, last_update_utc = @last_update_utc WHERE tenant_id = @tenant_id AND id = @id;",
                    cmd =>
                    {
                        PostgresqlCommandHelper.Add(cmd, "@kind", message.Kind.ToString());
                        PostgresqlCommandHelper.Add(cmd, "@content_text", message.ContentText);
                        PostgresqlCommandHelper.Add(cmd, "@thinking_text", message.ThinkingText);
                        PostgresqlCommandHelper.Add(cmd, "@proposal_id", message.ProposalId);
                        PostgresqlCommandHelper.Add(cmd, "@tracked_work_id", message.TrackedWorkId);
                        PostgresqlCommandHelper.Add(cmd, "@captain_id", message.CaptainId);
                        PostgresqlCommandHelper.Add(cmd, "@duration_ms", message.DurationMs);
                        BindMetrics(cmd, message);
                        PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", message.LastUpdateUtc);
                        PostgresqlCommandHelper.Add(cmd, "@tenant_id", message.TenantId);
                        PostgresqlCommandHelper.Add(cmd, "@id", message.Id);
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
                + " ORDER BY sequence DESC" + PostgresqlCommandHelper.Page(0, size + 1) + ";";

            List<AskMessage> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString, sql,
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@thread_id", threadId);
                    if (beforeSequence.HasValue) PostgresqlCommandHelper.Add(cmd, "@before", beforeSequence.Value);
                }, FromReader, token).ConfigureAwait(false);

            AskMessagePage page = new AskMessagePage();
            page.HasMore = rows.Count > size;
            page.Messages = rows.Take(size).OrderBy(m => m.Sequence).ToList();
            return page;
        }

        #endregion

        #region Private-Methods

        private static void Bind(NpgsqlCommand cmd, AskMessage message)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", message.Id);
            PostgresqlCommandHelper.Add(cmd, "@tenant_id", message.TenantId);
            PostgresqlCommandHelper.Add(cmd, "@user_id", message.UserId);
            PostgresqlCommandHelper.Add(cmd, "@thread_id", message.ThreadId);
            PostgresqlCommandHelper.Add(cmd, "@sequence", message.Sequence);
            PostgresqlCommandHelper.Add(cmd, "@role", message.Role.ToString());
            PostgresqlCommandHelper.Add(cmd, "@kind", message.Kind.ToString());
            PostgresqlCommandHelper.Add(cmd, "@content_text", message.ContentText);
            PostgresqlCommandHelper.Add(cmd, "@thinking_text", message.ThinkingText);
            PostgresqlCommandHelper.Add(cmd, "@proposal_id", message.ProposalId);
            PostgresqlCommandHelper.Add(cmd, "@tracked_work_id", message.TrackedWorkId);
            PostgresqlCommandHelper.Add(cmd, "@captain_id", message.CaptainId);
            PostgresqlCommandHelper.Add(cmd, "@duration_ms", message.DurationMs);
            BindMetrics(cmd, message);
            PostgresqlCommandHelper.AddDate(cmd, "@created_utc", message.CreatedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", message.LastUpdateUtc);
        }

        private static void BindMetrics(NpgsqlCommand cmd, AskMessage message)
        {
            CaptainChatMetrics? m = message.Metrics;
            PostgresqlCommandHelper.Add(cmd, "@ttft_ms", m?.TimeToFirstTokenMs);
            PostgresqlCommandHelper.Add(cmd, "@first_text_ms", m?.TimeToFirstTextMs);
            PostgresqlCommandHelper.Add(cmd, "@streaming_ms", m?.StreamingMs);
            PostgresqlCommandHelper.Add(cmd, "@tokens_per_second", m?.TokensPerSecond);
            PostgresqlCommandHelper.Add(cmd, "@input_tokens", m?.PromptTokens);
            PostgresqlCommandHelper.Add(cmd, "@output_tokens", m?.CompletionTokens);
            PostgresqlCommandHelper.Add(cmd, "@cached_tokens", m?.CachedTokens);
            PostgresqlCommandHelper.Add(cmd, "@tokens_estimated", m?.TokensEstimated);
            PostgresqlCommandHelper.Add(cmd, "@cost_usd", m?.CostUsd);
            PostgresqlCommandHelper.Add(cmd, "@tool_call_count", m?.ToolCallCount);
            PostgresqlCommandHelper.Add(cmd, "@tool_time_ms", m?.ToolTimeMs);
        }

        private static AskMessage FromReader(NpgsqlDataReader reader)
        {
            AskMessage message = new AskMessage();
            message.Id = reader["id"].ToString()!;
            message.TenantId = PostgresqlCommandHelper.ReadString(reader["tenant_id"]);
            message.UserId = PostgresqlCommandHelper.ReadString(reader["user_id"]);
            message.ThreadId = reader["thread_id"].ToString()!;
            message.Sequence = PostgresqlCommandHelper.ReadInt(reader["sequence"], 0);
            message.Role = PostgresqlCommandHelper.ReadEnum(reader["role"], AskMessageRoleEnum.User);
            message.Kind = PostgresqlCommandHelper.ReadEnum(reader["kind"], AskMessageKindEnum.Text);
            message.ContentText = reader["content_text"]?.ToString() ?? String.Empty;
            message.ThinkingText = PostgresqlCommandHelper.ReadString(reader["thinking_text"]);
            message.ProposalId = PostgresqlCommandHelper.ReadString(reader["proposal_id"]);
            message.TrackedWorkId = PostgresqlCommandHelper.ReadString(reader["tracked_work_id"]);
            message.CaptainId = PostgresqlCommandHelper.ReadString(reader["captain_id"]);
            message.DurationMs = PostgresqlCommandHelper.ReadNullableLong(reader["duration_ms"]);
            CaptainChatMetrics stored = new CaptainChatMetrics();
            stored.TimeToFirstTokenMs = PostgresqlCommandHelper.ReadNullableDouble(reader["ttft_ms"]);
            stored.TimeToFirstTextMs = PostgresqlCommandHelper.ReadNullableDouble(reader["first_text_ms"]);
            stored.StreamingMs = PostgresqlCommandHelper.ReadNullableDouble(reader["streaming_ms"]);
            stored.TokensPerSecond = PostgresqlCommandHelper.ReadNullableDouble(reader["tokens_per_second"]);
            stored.PromptTokens = PostgresqlCommandHelper.ReadNullableInt(reader["input_tokens"]);
            stored.CompletionTokens = PostgresqlCommandHelper.ReadNullableInt(reader["output_tokens"]);
            stored.CachedTokens = PostgresqlCommandHelper.ReadNullableInt(reader["cached_tokens"]);
            stored.TokensEstimated = PostgresqlCommandHelper.ReadNullableBool(reader["tokens_estimated"]);
            stored.CostUsd = PostgresqlCommandHelper.ReadNullableDouble(reader["cost_usd"]);
            stored.ToolCallCount = PostgresqlCommandHelper.ReadNullableInt(reader["tool_call_count"]);
            stored.ToolTimeMs = PostgresqlCommandHelper.ReadNullableDouble(reader["tool_time_ms"]);
            message.Metrics = AskMessageMetricsColumns.FromColumns(stored, message.DurationMs);
            message.CreatedUtc = PostgresqlCommandHelper.ReadDate(reader["created_utc"]);
            message.LastUpdateUtc = PostgresqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return message;
        }

        #endregion
    }
}
