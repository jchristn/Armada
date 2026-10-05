namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of CLI permission request persistence.
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
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when connectionString is null.</exception>
        public CliPermissionRequestMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<CliPermissionRequest> CreateAsync(CliPermissionRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            request.LastUpdateUtc = DateTime.UtcNow;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, request), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return request;
        }

        /// <inheritdoc />
        public async Task<CliPermissionRequest?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<CliPermissionRequest> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM cli_permission_requests WHERE id = @id;",
                cmd => MysqlCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task UpdateMessageAsync(string id, string? messageId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE cli_permission_requests SET message_id = @message_id, last_update_utc = @now WHERE id = @id;",
                    cmd =>
                    {
                        MysqlCommandHelper.Add(cmd, "@message_id", messageId);
                        MysqlCommandHelper.AddDate(cmd, "@now", DateTime.UtcNow);
                        MysqlCommandHelper.Add(cmd, "@id", id);
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
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                updated = await MysqlCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE cli_permission_requests SET status = @status, decision_source = @source, rule_id = @rule_id, decided_by_user_id = @decided_by_user_id, decision_message = @message, decided_utc = @now, last_update_utc = @now WHERE id = @id AND status = @pending;",
                    cmd =>
                    {
                        MysqlCommandHelper.Add(cmd, "@status", status.ToString());
                        MysqlCommandHelper.Add(cmd, "@source", source.ToString());
                        MysqlCommandHelper.Add(cmd, "@rule_id", ruleId);
                        MysqlCommandHelper.Add(cmd, "@decided_by_user_id", decidedByUserId);
                        MysqlCommandHelper.Add(cmd, "@message", message);
                        MysqlCommandHelper.AddDate(cmd, "@now", now);
                        MysqlCommandHelper.Add(cmd, "@id", id);
                        MysqlCommandHelper.Add(cmd, "@pending", CliPermissionRequestStatusEnum.Pending.ToString());
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

            return await MysqlCommandHelper.QueryAsync(_ConnectionString, sql.ToString(), cmd =>
            {
                if (query.TenantId != null) MysqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.UserId != null) MysqlCommandHelper.Add(cmd, "@user_id", query.UserId);
                if (query.Status.HasValue) MysqlCommandHelper.Add(cmd, "@status", query.Status.Value.ToString());
                if (query.MissionId != null) MysqlCommandHelper.Add(cmd, "@mission_id", query.MissionId);
                if (query.ThreadId != null) MysqlCommandHelper.Add(cmd, "@thread_id", query.ThreadId);
                if (query.CaptainId != null) MysqlCommandHelper.Add(cmd, "@captain_id", query.CaptainId);
                if (query.VesselId != null) MysqlCommandHelper.Add(cmd, "@vessel_id", query.VesselId);
            }, FromReader, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(MySqlCommand cmd, CliPermissionRequest request)
        {
            MysqlCommandHelper.Add(cmd, "@id", request.Id);
            MysqlCommandHelper.Add(cmd, "@tenant_id", request.TenantId);
            MysqlCommandHelper.Add(cmd, "@user_id", request.UserId);
            MysqlCommandHelper.Add(cmd, "@captain_id", request.CaptainId);
            MysqlCommandHelper.Add(cmd, "@mission_id", request.MissionId);
            MysqlCommandHelper.Add(cmd, "@voyage_id", request.VoyageId);
            MysqlCommandHelper.Add(cmd, "@vessel_id", request.VesselId);
            MysqlCommandHelper.Add(cmd, "@thread_id", request.ThreadId);
            MysqlCommandHelper.Add(cmd, "@message_id", request.MessageId);
            MysqlCommandHelper.Add(cmd, "@runtime", request.Runtime.ToString());
            MysqlCommandHelper.Add(cmd, "@tool_name", request.ToolName);
            MysqlCommandHelper.Add(cmd, "@input_text", request.InputText);
            MysqlCommandHelper.Add(cmd, "@summary_text", request.SummaryText);
            MysqlCommandHelper.Add(cmd, "@suggested_rule", request.SuggestedRule);
            MysqlCommandHelper.Add(cmd, "@status", request.Status.ToString());
            MysqlCommandHelper.Add(cmd, "@decision_source", request.DecisionSource?.ToString());
            MysqlCommandHelper.Add(cmd, "@rule_id", request.RuleId);
            MysqlCommandHelper.Add(cmd, "@decided_by_user_id", request.DecidedByUserId);
            MysqlCommandHelper.Add(cmd, "@decision_message", request.DecisionMessage);
            MysqlCommandHelper.AddDate(cmd, "@expires_utc", request.ExpiresUtc);
            MysqlCommandHelper.AddDate(cmd, "@decided_utc", request.DecidedUtc);
            MysqlCommandHelper.AddDate(cmd, "@created_utc", request.CreatedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", request.LastUpdateUtc);
        }

        private static CliPermissionRequest FromReader(MySqlDataReader reader)
        {
            CliPermissionRequest request = new CliPermissionRequest();
            request.Id = reader["id"].ToString()!;
            request.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            request.UserId = MysqlCommandHelper.ReadString(reader["user_id"]);
            request.CaptainId = MysqlCommandHelper.ReadString(reader["captain_id"]);
            request.MissionId = MysqlCommandHelper.ReadString(reader["mission_id"]);
            request.VoyageId = MysqlCommandHelper.ReadString(reader["voyage_id"]);
            request.VesselId = MysqlCommandHelper.ReadString(reader["vessel_id"]);
            request.ThreadId = MysqlCommandHelper.ReadString(reader["thread_id"]);
            request.MessageId = MysqlCommandHelper.ReadString(reader["message_id"]);
            request.Runtime = MysqlCommandHelper.ReadEnum(reader["runtime"], AgentRuntimeEnum.ClaudeCode);
            request.ToolName = reader["tool_name"]?.ToString() ?? String.Empty;
            request.InputText = reader["input_text"]?.ToString() ?? "{}";
            request.SummaryText = reader["summary_text"]?.ToString() ?? String.Empty;
            request.SuggestedRule = MysqlCommandHelper.ReadString(reader["suggested_rule"]);
            request.Status = MysqlCommandHelper.ReadEnum(reader["status"], CliPermissionRequestStatusEnum.Pending);
            string? source = MysqlCommandHelper.ReadString(reader["decision_source"]);
            request.DecisionSource = source != null && Enum.TryParse<CliPermissionDecisionSourceEnum>(source, true, out CliPermissionDecisionSourceEnum parsed) ? parsed : (CliPermissionDecisionSourceEnum?)null;
            request.RuleId = MysqlCommandHelper.ReadString(reader["rule_id"]);
            request.DecidedByUserId = MysqlCommandHelper.ReadString(reader["decided_by_user_id"]);
            request.DecisionMessage = MysqlCommandHelper.ReadString(reader["decision_message"]);
            request.ExpiresUtc = MysqlCommandHelper.ReadDate(reader["expires_utc"]);
            request.DecidedUtc = MysqlCommandHelper.ReadNullableDate(reader["decided_utc"]);
            request.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            request.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return request;
        }

        #endregion
    }
}
