namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
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
    /// SQLite implementation of CLI permission request persistence.
    /// </summary>
    public class CliPermissionRequestMethods : ICliPermissionRequestMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO cli_permission_requests
            (id, tenant_id, user_id, captain_id, mission_id, voyage_id, vessel_id, thread_id, message_id, runtime, tool_name, input_text, summary_text, suggested_rule, status, decision_source, rule_id, decided_by_user_id, decision_message, expires_utc, decided_utc, created_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @user_id, @captain_id, @mission_id, @voyage_id, @vessel_id, @thread_id, @message_id, @runtime, @tool_name, @input_text, @summary_text, @suggested_rule, @status, @decision_source, @rule_id, @decided_by_user_id, @decision_message, @expires_utc, @decided_utc, @created_utc, @last_update_utc);";

        private readonly string _ConnectionString;
        private readonly SemaphoreSlim? _WriteLock;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQLite database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public CliPermissionRequestMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _ConnectionString = driver.ConnectionString;
            _WriteLock = driver.WriteLock;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<CliPermissionRequest> CreateAsync(CliPermissionRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            request.LastUpdateUtc = DateTime.UtcNow;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, request), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return request;
        }

        /// <inheritdoc />
        public async Task<CliPermissionRequest?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<CliPermissionRequest> rows = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM cli_permission_requests WHERE id = @id;",
                cmd => SqliteCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task UpdateMessageAsync(string id, string? messageId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE cli_permission_requests SET message_id = @message_id, last_update_utc = @now WHERE id = @id;",
                    cmd =>
                    {
                        SqliteCommandHelper.Add(cmd, "@message_id", messageId);
                        SqliteCommandHelper.AddDate(cmd, "@now", DateTime.UtcNow);
                        SqliteCommandHelper.Add(cmd, "@id", id);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> TryDecideAsync(string id, CliPermissionRequestStatusEnum status, CliPermissionDecisionSourceEnum source, string? ruleId, string? decidedByUserId, string? message, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            if (status == CliPermissionRequestStatusEnum.Pending) throw new ArgumentException("A decision cannot set Pending.", nameof(status));

            int updated = 0;
            DateTime now = DateTime.UtcNow;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                updated = await SqliteCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE cli_permission_requests SET status = @status, decision_source = @source, rule_id = @rule_id, decided_by_user_id = @decided_by_user_id, decision_message = @message, decided_utc = @now, last_update_utc = @now WHERE id = @id AND status = @pending;",
                    cmd =>
                    {
                        SqliteCommandHelper.Add(cmd, "@status", status.ToString());
                        SqliteCommandHelper.Add(cmd, "@source", source.ToString());
                        SqliteCommandHelper.Add(cmd, "@rule_id", ruleId);
                        SqliteCommandHelper.Add(cmd, "@decided_by_user_id", decidedByUserId);
                        SqliteCommandHelper.Add(cmd, "@message", message);
                        SqliteCommandHelper.AddDate(cmd, "@now", now);
                        SqliteCommandHelper.Add(cmd, "@id", id);
                        SqliteCommandHelper.Add(cmd, "@pending", CliPermissionRequestStatusEnum.Pending.ToString());
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return updated == 1;
        }

        /// <inheritdoc />
        public async Task<List<CliPermissionRequest>> EnumerateAsync(CliPermissionRequestQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            StringBuilder sql = new StringBuilder("SELECT * FROM cli_permission_requests WHERE 1 = 1");
            if (query.TenantId != null) sql.Append(" AND tenant_id = @tenant_id");
            if (query.UserId != null) sql.Append(" AND user_id = @user_id");
            if (query.Status.HasValue) sql.Append(" AND status = @status");
            if (query.MissionId != null) sql.Append(" AND mission_id = @mission_id");
            if (query.ThreadId != null) sql.Append(" AND thread_id = @thread_id");
            if (query.CaptainId != null) sql.Append(" AND captain_id = @captain_id");
            if (query.VesselId != null) sql.Append(" AND vessel_id = @vessel_id");
            sql.Append(" ORDER BY created_utc DESC, id DESC LIMIT " + query.Limit + ";");

            return await SqliteCommandHelper.QueryAsync(_ConnectionString, sql.ToString(), cmd =>
            {
                if (query.TenantId != null) SqliteCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.UserId != null) SqliteCommandHelper.Add(cmd, "@user_id", query.UserId);
                if (query.Status.HasValue) SqliteCommandHelper.Add(cmd, "@status", query.Status.Value.ToString());
                if (query.MissionId != null) SqliteCommandHelper.Add(cmd, "@mission_id", query.MissionId);
                if (query.ThreadId != null) SqliteCommandHelper.Add(cmd, "@thread_id", query.ThreadId);
                if (query.CaptainId != null) SqliteCommandHelper.Add(cmd, "@captain_id", query.CaptainId);
                if (query.VesselId != null) SqliteCommandHelper.Add(cmd, "@vessel_id", query.VesselId);
            }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> DeleteFinishedBeforeAsync(DateTime cutoffUtc, CancellationToken token = default)
        {
            int deleted = 0;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                deleted = await SqliteCommandHelper.ExecuteAsync(conn, tx,
                    "DELETE FROM cli_permission_requests WHERE status <> @pending AND (decided_utc < @cutoff OR (decided_utc IS NULL AND created_utc < @cutoff));",
                    cmd =>
                    {
                        SqliteCommandHelper.Add(cmd, "@pending", CliPermissionRequestStatusEnum.Pending.ToString());
                        SqliteCommandHelper.AddDate(cmd, "@cutoff", cutoffUtc);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqliteCommand cmd, CliPermissionRequest request)
        {
            SqliteCommandHelper.Add(cmd, "@id", request.Id);
            SqliteCommandHelper.Add(cmd, "@tenant_id", request.TenantId);
            SqliteCommandHelper.Add(cmd, "@user_id", request.UserId);
            SqliteCommandHelper.Add(cmd, "@captain_id", request.CaptainId);
            SqliteCommandHelper.Add(cmd, "@mission_id", request.MissionId);
            SqliteCommandHelper.Add(cmd, "@voyage_id", request.VoyageId);
            SqliteCommandHelper.Add(cmd, "@vessel_id", request.VesselId);
            SqliteCommandHelper.Add(cmd, "@thread_id", request.ThreadId);
            SqliteCommandHelper.Add(cmd, "@message_id", request.MessageId);
            SqliteCommandHelper.Add(cmd, "@runtime", request.Runtime.ToString());
            SqliteCommandHelper.Add(cmd, "@tool_name", request.ToolName);
            SqliteCommandHelper.Add(cmd, "@input_text", request.InputText);
            SqliteCommandHelper.Add(cmd, "@summary_text", request.SummaryText);
            SqliteCommandHelper.Add(cmd, "@suggested_rule", request.SuggestedRule);
            SqliteCommandHelper.Add(cmd, "@status", request.Status.ToString());
            SqliteCommandHelper.Add(cmd, "@decision_source", request.DecisionSource?.ToString());
            SqliteCommandHelper.Add(cmd, "@rule_id", request.RuleId);
            SqliteCommandHelper.Add(cmd, "@decided_by_user_id", request.DecidedByUserId);
            SqliteCommandHelper.Add(cmd, "@decision_message", request.DecisionMessage);
            SqliteCommandHelper.AddDate(cmd, "@expires_utc", request.ExpiresUtc);
            SqliteCommandHelper.AddDate(cmd, "@decided_utc", request.DecidedUtc);
            SqliteCommandHelper.AddDate(cmd, "@created_utc", request.CreatedUtc);
            SqliteCommandHelper.AddDate(cmd, "@last_update_utc", request.LastUpdateUtc);
        }

        private static CliPermissionRequest FromReader(SqliteDataReader reader)
        {
            CliPermissionRequest request = new CliPermissionRequest();
            request.Id = reader["id"].ToString()!;
            request.TenantId = SqliteCommandHelper.ReadString(reader["tenant_id"]);
            request.UserId = SqliteCommandHelper.ReadString(reader["user_id"]);
            request.CaptainId = SqliteCommandHelper.ReadString(reader["captain_id"]);
            request.MissionId = SqliteCommandHelper.ReadString(reader["mission_id"]);
            request.VoyageId = SqliteCommandHelper.ReadString(reader["voyage_id"]);
            request.VesselId = SqliteCommandHelper.ReadString(reader["vessel_id"]);
            request.ThreadId = SqliteCommandHelper.ReadString(reader["thread_id"]);
            request.MessageId = SqliteCommandHelper.ReadString(reader["message_id"]);
            request.Runtime = SqliteCommandHelper.ReadEnum(reader["runtime"], AgentRuntimeEnum.ClaudeCode);
            request.ToolName = reader["tool_name"]?.ToString() ?? String.Empty;
            request.InputText = reader["input_text"]?.ToString() ?? "{}";
            request.SummaryText = reader["summary_text"]?.ToString() ?? String.Empty;
            request.SuggestedRule = SqliteCommandHelper.ReadString(reader["suggested_rule"]);
            request.Status = SqliteCommandHelper.ReadEnum(reader["status"], CliPermissionRequestStatusEnum.Pending);
            string? source = SqliteCommandHelper.ReadString(reader["decision_source"]);
            request.DecisionSource = source != null && Enum.TryParse<CliPermissionDecisionSourceEnum>(source, true, out CliPermissionDecisionSourceEnum parsed) ? parsed : (CliPermissionDecisionSourceEnum?)null;
            request.RuleId = SqliteCommandHelper.ReadString(reader["rule_id"]);
            request.DecidedByUserId = SqliteCommandHelper.ReadString(reader["decided_by_user_id"]);
            request.DecisionMessage = SqliteCommandHelper.ReadString(reader["decision_message"]);
            request.ExpiresUtc = SqliteCommandHelper.ReadDate(reader["expires_utc"]);
            request.DecidedUtc = SqliteCommandHelper.ReadNullableDate(reader["decided_utc"]);
            request.CreatedUtc = SqliteCommandHelper.ReadDate(reader["created_utc"]);
            request.LastUpdateUtc = SqliteCommandHelper.ReadDate(reader["last_update_utc"]);
            return request;
        }

        #endregion
    }
}
