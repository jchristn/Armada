namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
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
    /// SQL Server implementation of CLI permission request persistence.
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
        /// <param name="driver">SQL Server database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public CliPermissionRequestMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<CliPermissionRequest> CreateAsync(CliPermissionRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            request.LastUpdateUtc = DateTime.UtcNow;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, request), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return request;
        }

        /// <inheritdoc />
        public async Task<CliPermissionRequest?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<CliPermissionRequest> rows = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM cli_permission_requests WHERE id = @id;",
                cmd => SqlServerCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task UpdateMessageAsync(string id, string? messageId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE cli_permission_requests SET message_id = @message_id, last_update_utc = @now WHERE id = @id;",
                    cmd =>
                    {
                        SqlServerCommandHelper.Add(cmd, "@message_id", messageId);
                        SqlServerCommandHelper.AddDate(cmd, "@now", DateTime.UtcNow);
                        SqlServerCommandHelper.Add(cmd, "@id", id);
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
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                updated = await SqlServerCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE cli_permission_requests SET status = @status, decision_source = @source, rule_id = @rule_id, decided_by_user_id = @decided_by_user_id, decision_message = @message, decided_utc = @now, last_update_utc = @now WHERE id = @id AND status = @pending;",
                    cmd =>
                    {
                        SqlServerCommandHelper.Add(cmd, "@status", status.ToString());
                        SqlServerCommandHelper.Add(cmd, "@source", source.ToString());
                        SqlServerCommandHelper.Add(cmd, "@rule_id", ruleId);
                        SqlServerCommandHelper.Add(cmd, "@decided_by_user_id", decidedByUserId);
                        SqlServerCommandHelper.Add(cmd, "@message", message);
                        SqlServerCommandHelper.AddDate(cmd, "@now", now);
                        SqlServerCommandHelper.Add(cmd, "@id", id);
                        SqlServerCommandHelper.Add(cmd, "@pending", CliPermissionRequestStatusEnum.Pending.ToString());
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return updated == 1;
        }

        /// <inheritdoc />
        public async Task<List<CliPermissionRequest>> EnumerateAsync(CliPermissionRequestQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            StringBuilder sql = new StringBuilder("SELECT TOP (" + query.Limit + ") * FROM cli_permission_requests WHERE 1 = 1");
            if (query.TenantId != null) sql.Append(" AND tenant_id = @tenant_id");
            if (query.UserId != null) sql.Append(" AND user_id = @user_id");
            if (query.Status.HasValue) sql.Append(" AND status = @status");
            if (query.MissionId != null) sql.Append(" AND mission_id = @mission_id");
            if (query.ThreadId != null) sql.Append(" AND thread_id = @thread_id");
            if (query.CaptainId != null) sql.Append(" AND captain_id = @captain_id");
            if (query.VesselId != null) sql.Append(" AND vessel_id = @vessel_id");
            sql.Append(" ORDER BY created_utc DESC, id DESC;");

            return await SqlServerCommandHelper.QueryAsync(_ConnectionString, sql.ToString(), cmd =>
            {
                if (query.TenantId != null) SqlServerCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.UserId != null) SqlServerCommandHelper.Add(cmd, "@user_id", query.UserId);
                if (query.Status.HasValue) SqlServerCommandHelper.Add(cmd, "@status", query.Status.Value.ToString());
                if (query.MissionId != null) SqlServerCommandHelper.Add(cmd, "@mission_id", query.MissionId);
                if (query.ThreadId != null) SqlServerCommandHelper.Add(cmd, "@thread_id", query.ThreadId);
                if (query.CaptainId != null) SqlServerCommandHelper.Add(cmd, "@captain_id", query.CaptainId);
                if (query.VesselId != null) SqlServerCommandHelper.Add(cmd, "@vessel_id", query.VesselId);
            }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> DeleteFinishedBeforeAsync(DateTime cutoffUtc, CancellationToken token = default)
        {
            int deleted = 0;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                deleted = await SqlServerCommandHelper.ExecuteAsync(conn, tx,
                    "DELETE FROM cli_permission_requests WHERE status <> @pending AND (decided_utc < @cutoff OR (decided_utc IS NULL AND created_utc < @cutoff));",
                    cmd =>
                    {
                        SqlServerCommandHelper.Add(cmd, "@pending", CliPermissionRequestStatusEnum.Pending.ToString());
                        SqlServerCommandHelper.AddDate(cmd, "@cutoff", cutoffUtc);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqlCommand cmd, CliPermissionRequest request)
        {
            SqlServerCommandHelper.Add(cmd, "@id", request.Id);
            SqlServerCommandHelper.Add(cmd, "@tenant_id", request.TenantId);
            SqlServerCommandHelper.Add(cmd, "@user_id", request.UserId);
            SqlServerCommandHelper.Add(cmd, "@captain_id", request.CaptainId);
            SqlServerCommandHelper.Add(cmd, "@mission_id", request.MissionId);
            SqlServerCommandHelper.Add(cmd, "@voyage_id", request.VoyageId);
            SqlServerCommandHelper.Add(cmd, "@vessel_id", request.VesselId);
            SqlServerCommandHelper.Add(cmd, "@thread_id", request.ThreadId);
            SqlServerCommandHelper.Add(cmd, "@message_id", request.MessageId);
            SqlServerCommandHelper.Add(cmd, "@runtime", request.Runtime.ToString());
            SqlServerCommandHelper.Add(cmd, "@tool_name", request.ToolName);
            SqlServerCommandHelper.Add(cmd, "@input_text", request.InputText);
            SqlServerCommandHelper.Add(cmd, "@summary_text", request.SummaryText);
            SqlServerCommandHelper.Add(cmd, "@suggested_rule", request.SuggestedRule);
            SqlServerCommandHelper.Add(cmd, "@status", request.Status.ToString());
            SqlServerCommandHelper.Add(cmd, "@decision_source", request.DecisionSource?.ToString());
            SqlServerCommandHelper.Add(cmd, "@rule_id", request.RuleId);
            SqlServerCommandHelper.Add(cmd, "@decided_by_user_id", request.DecidedByUserId);
            SqlServerCommandHelper.Add(cmd, "@decision_message", request.DecisionMessage);
            SqlServerCommandHelper.AddDate(cmd, "@expires_utc", request.ExpiresUtc);
            SqlServerCommandHelper.AddDate(cmd, "@decided_utc", request.DecidedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@created_utc", request.CreatedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", request.LastUpdateUtc);
        }

        private static CliPermissionRequest FromReader(SqlDataReader reader)
        {
            CliPermissionRequest request = new CliPermissionRequest();
            request.Id = reader["id"].ToString()!;
            request.TenantId = SqlServerCommandHelper.ReadString(reader["tenant_id"]);
            request.UserId = SqlServerCommandHelper.ReadString(reader["user_id"]);
            request.CaptainId = SqlServerCommandHelper.ReadString(reader["captain_id"]);
            request.MissionId = SqlServerCommandHelper.ReadString(reader["mission_id"]);
            request.VoyageId = SqlServerCommandHelper.ReadString(reader["voyage_id"]);
            request.VesselId = SqlServerCommandHelper.ReadString(reader["vessel_id"]);
            request.ThreadId = SqlServerCommandHelper.ReadString(reader["thread_id"]);
            request.MessageId = SqlServerCommandHelper.ReadString(reader["message_id"]);
            request.Runtime = SqlServerCommandHelper.ReadEnum(reader["runtime"], AgentRuntimeEnum.ClaudeCode);
            request.ToolName = reader["tool_name"]?.ToString() ?? String.Empty;
            request.InputText = reader["input_text"]?.ToString() ?? "{}";
            request.SummaryText = reader["summary_text"]?.ToString() ?? String.Empty;
            request.SuggestedRule = SqlServerCommandHelper.ReadString(reader["suggested_rule"]);
            request.Status = SqlServerCommandHelper.ReadEnum(reader["status"], CliPermissionRequestStatusEnum.Pending);
            string? source = SqlServerCommandHelper.ReadString(reader["decision_source"]);
            request.DecisionSource = source != null && Enum.TryParse<CliPermissionDecisionSourceEnum>(source, true, out CliPermissionDecisionSourceEnum parsed) ? parsed : (CliPermissionDecisionSourceEnum?)null;
            request.RuleId = SqlServerCommandHelper.ReadString(reader["rule_id"]);
            request.DecidedByUserId = SqlServerCommandHelper.ReadString(reader["decided_by_user_id"]);
            request.DecisionMessage = SqlServerCommandHelper.ReadString(reader["decision_message"]);
            request.ExpiresUtc = SqlServerCommandHelper.ReadDate(reader["expires_utc"]);
            request.DecidedUtc = SqlServerCommandHelper.ReadNullableDate(reader["decided_utc"]);
            request.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            request.LastUpdateUtc = SqlServerCommandHelper.ReadDate(reader["last_update_utc"]);
            return request;
        }

        #endregion
    }
}
