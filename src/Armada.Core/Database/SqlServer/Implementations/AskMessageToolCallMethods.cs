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
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQL Server implementation of Ask Armada message tool-call persistence.
    /// </summary>
    public class AskMessageToolCallMethods : IAskMessageToolCallMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO ask_message_tool_calls
            (id, tenant_id, user_id, message_id, thread_id, call_id, tool_name, arguments_text, result_text, ok, elapsed_ms, created_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @user_id, @message_id, @thread_id, @call_id, @tool_name, @arguments_text, @result_text, @ok, @elapsed_ms, @created_utc, @last_update_utc);";

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
        public AskMessageToolCallMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<List<AskMessageToolCall>> CreateManyAsync(List<AskMessageToolCall> calls, CancellationToken token = default)
        {
            if (calls == null) throw new ArgumentNullException(nameof(calls));
            if (calls.Count == 0) return calls;

            DateTime now = DateTime.UtcNow;
            foreach (AskMessageToolCall call in calls)
            {
                if (call == null) throw new ArgumentException("Tool calls must not contain null entries.", nameof(calls));
                if (String.IsNullOrEmpty(call.TenantId) || String.IsNullOrEmpty(call.ThreadId) || String.IsNullOrEmpty(call.MessageId))
                    throw new ArgumentException("TenantId, ThreadId, and MessageId are required on every tool call.", nameof(calls));
                call.LastUpdateUtc = now;
            }

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                foreach (AskMessageToolCall call in calls)
                {
                    token.ThrowIfCancellationRequested();
                    await SqlServerCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, call), token).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);
            return calls;
        }

        /// <inheritdoc />
        public async Task<List<AskMessageToolCall>> EnumerateByMessagesAsync(string tenantId, string threadId, List<string> messageIds, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(threadId)) throw new ArgumentNullException(nameof(threadId));
            List<AskMessageToolCall> results = new List<AskMessageToolCall>();
            if (messageIds == null || messageIds.Count == 0) return results;

            List<string> distinct = messageIds.Where(m => !String.IsNullOrEmpty(m)).Distinct(StringComparer.Ordinal).ToList();
            for (int start = 0; start < distinct.Count; start += 200)
            {
                List<string> chunk = distinct.Skip(start).Take(200).ToList();
                List<string> names = new List<string>();
                for (int i = 0; i < chunk.Count; i++) names.Add("@m" + i);

                List<AskMessageToolCall> rows = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                    "SELECT * FROM ask_message_tool_calls WHERE tenant_id = @tenant_id AND thread_id = @thread_id AND message_id IN (" + String.Join(", ", names) + ") ORDER BY created_utc ASC, id ASC;",
                    cmd =>
                    {
                        SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                        SqlServerCommandHelper.Add(cmd, "@thread_id", threadId);
                        for (int i = 0; i < chunk.Count; i++) SqlServerCommandHelper.Add(cmd, "@m" + i, chunk[i]);
                    }, FromReader, token).ConfigureAwait(false);
                results.AddRange(rows);
            }

            return results;
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqlCommand cmd, AskMessageToolCall call)
        {
            SqlServerCommandHelper.Add(cmd, "@id", call.Id);
            SqlServerCommandHelper.Add(cmd, "@tenant_id", call.TenantId);
            SqlServerCommandHelper.Add(cmd, "@user_id", call.UserId);
            SqlServerCommandHelper.Add(cmd, "@message_id", call.MessageId);
            SqlServerCommandHelper.Add(cmd, "@thread_id", call.ThreadId);
            SqlServerCommandHelper.Add(cmd, "@call_id", call.CallId);
            SqlServerCommandHelper.Add(cmd, "@tool_name", call.ToolName);
            SqlServerCommandHelper.Add(cmd, "@arguments_text", call.ArgumentsText);
            SqlServerCommandHelper.Add(cmd, "@result_text", call.ResultText);
            SqlServerCommandHelper.Add(cmd, "@ok", call.Ok);
            SqlServerCommandHelper.Add(cmd, "@elapsed_ms", call.ElapsedMs);
            SqlServerCommandHelper.AddDate(cmd, "@created_utc", call.CreatedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", call.LastUpdateUtc);
        }

        private static AskMessageToolCall FromReader(SqlDataReader reader)
        {
            AskMessageToolCall call = new AskMessageToolCall();
            call.Id = reader["id"].ToString()!;
            call.TenantId = SqlServerCommandHelper.ReadString(reader["tenant_id"]);
            call.UserId = SqlServerCommandHelper.ReadString(reader["user_id"]);
            call.MessageId = reader["message_id"].ToString()!;
            call.ThreadId = reader["thread_id"].ToString()!;
            call.CallId = SqlServerCommandHelper.ReadString(reader["call_id"]);
            call.ToolName = reader["tool_name"].ToString()!;
            call.ArgumentsText = SqlServerCommandHelper.ReadString(reader["arguments_text"]);
            call.ResultText = SqlServerCommandHelper.ReadString(reader["result_text"]);
            call.Ok = SqlServerCommandHelper.ReadNullableBool(reader["ok"]);
            call.ElapsedMs = SqlServerCommandHelper.ReadNullableLong(reader["elapsed_ms"]);
            call.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            call.LastUpdateUtc = SqlServerCommandHelper.ReadDate(reader["last_update_utc"]);
            return call;
        }

        #endregion
    }
}
