namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of Ask Armada action proposal persistence.
    /// </summary>
    public class AskActionProposalMethods : IAskActionProposalMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO ask_action_proposals
            (id, tenant_id, user_id, thread_id, message_id, tool_name, arguments_text, summary_text, source, status, result_text, error_text, decided_by_user_id, decided_utc, executed_utc, created_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @user_id, @thread_id, @message_id, @tool_name, @arguments_text, @summary_text, @source, @status, @result_text, @error_text, @decided_by_user_id, @decided_utc, @executed_utc, @created_utc, @last_update_utc);";

        private static readonly string _Update = @"UPDATE ask_action_proposals SET
            message_id = @message_id, summary_text = @summary_text, status = @status, result_text = @result_text, error_text = @error_text,
            decided_by_user_id = @decided_by_user_id, decided_utc = @decided_utc, executed_utc = @executed_utc, last_update_utc = @last_update_utc
            WHERE tenant_id = @tenant_id AND id = @id;";

        private readonly string _ConnectionString;
        private readonly SemaphoreSlim? _WriteLock;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when connectionString is null.</exception>
        public AskActionProposalMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<AskActionProposal> CreateAsync(AskActionProposal proposal, CancellationToken token = default)
        {
            if (proposal == null) throw new ArgumentNullException(nameof(proposal));
            if (String.IsNullOrEmpty(proposal.TenantId)) throw new ArgumentException("TenantId is required.", nameof(proposal));
            if (String.IsNullOrEmpty(proposal.ThreadId)) throw new ArgumentException("ThreadId is required.", nameof(proposal));

            proposal.LastUpdateUtc = DateTime.UtcNow;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, proposal), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return proposal;
        }

        /// <inheritdoc />
        public async Task<AskActionProposal?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            List<AskActionProposal> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_action_proposals WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@id", id);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<AskActionProposal> UpdateAsync(AskActionProposal proposal, CancellationToken token = default)
        {
            if (proposal == null) throw new ArgumentNullException(nameof(proposal));
            if (String.IsNullOrEmpty(proposal.TenantId)) throw new ArgumentException("TenantId is required.", nameof(proposal));

            proposal.LastUpdateUtc = DateTime.UtcNow;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Update, cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@message_id", proposal.MessageId);
                    MysqlCommandHelper.Add(cmd, "@summary_text", proposal.SummaryText);
                    MysqlCommandHelper.Add(cmd, "@status", proposal.Status.ToString());
                    MysqlCommandHelper.Add(cmd, "@result_text", proposal.ResultText);
                    MysqlCommandHelper.Add(cmd, "@error_text", proposal.ErrorText);
                    MysqlCommandHelper.Add(cmd, "@decided_by_user_id", proposal.DecidedByUserId);
                    MysqlCommandHelper.AddDate(cmd, "@decided_utc", proposal.DecidedUtc);
                    MysqlCommandHelper.AddDate(cmd, "@executed_utc", proposal.ExecutedUtc);
                    MysqlCommandHelper.AddDate(cmd, "@last_update_utc", proposal.LastUpdateUtc);
                    MysqlCommandHelper.Add(cmd, "@tenant_id", proposal.TenantId);
                    MysqlCommandHelper.Add(cmd, "@id", proposal.Id);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return proposal;
        }

        /// <inheritdoc />
        public async Task<bool> TryTransitionAsync(string tenantId, string id, AskProposalStatusEnum from, AskProposalStatusEnum to, string? decidedByUserId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            int updated = 0;
            DateTime now = DateTime.UtcNow;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                updated = await MysqlCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE ask_action_proposals SET status = @to, decided_by_user_id = @decided_by_user_id, decided_utc = @now, last_update_utc = @now WHERE tenant_id = @tenant_id AND id = @id AND status = @from;",
                    cmd =>
                    {
                        MysqlCommandHelper.Add(cmd, "@to", to.ToString());
                        MysqlCommandHelper.Add(cmd, "@decided_by_user_id", decidedByUserId);
                        MysqlCommandHelper.AddDate(cmd, "@now", now);
                        MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                        MysqlCommandHelper.Add(cmd, "@id", id);
                        MysqlCommandHelper.Add(cmd, "@from", from.ToString());
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return updated == 1;
        }

        /// <inheritdoc />
        public async Task<List<AskActionProposal>> EnumerateByThreadAsync(string tenantId, string threadId, AskProposalStatusEnum? status, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(threadId)) throw new ArgumentNullException(nameof(threadId));

            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_action_proposals WHERE tenant_id = @tenant_id AND thread_id = @thread_id" + (status.HasValue ? " AND status = @status" : String.Empty) + " ORDER BY created_utc ASC, id ASC;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@thread_id", threadId);
                    if (status.HasValue) MysqlCommandHelper.Add(cmd, "@status", status.Value.ToString());
                }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<AskActionProposal>> EnumeratePendingBeforeAsync(DateTime cutoffUtc, CancellationToken token = default)
        {
            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_action_proposals WHERE status = @status AND created_utc < @cutoff ORDER BY created_utc ASC, id ASC;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@status", AskProposalStatusEnum.Pending.ToString());
                    MysqlCommandHelper.AddDate(cmd, "@cutoff", cutoffUtc);
                }, FromReader, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(MySqlCommand cmd, AskActionProposal proposal)
        {
            MysqlCommandHelper.Add(cmd, "@id", proposal.Id);
            MysqlCommandHelper.Add(cmd, "@tenant_id", proposal.TenantId);
            MysqlCommandHelper.Add(cmd, "@user_id", proposal.UserId);
            MysqlCommandHelper.Add(cmd, "@thread_id", proposal.ThreadId);
            MysqlCommandHelper.Add(cmd, "@message_id", proposal.MessageId);
            MysqlCommandHelper.Add(cmd, "@tool_name", proposal.ToolName);
            MysqlCommandHelper.Add(cmd, "@arguments_text", proposal.ArgumentsText);
            MysqlCommandHelper.Add(cmd, "@summary_text", proposal.SummaryText);
            MysqlCommandHelper.Add(cmd, "@source", proposal.Source.ToString());
            MysqlCommandHelper.Add(cmd, "@status", proposal.Status.ToString());
            MysqlCommandHelper.Add(cmd, "@result_text", proposal.ResultText);
            MysqlCommandHelper.Add(cmd, "@error_text", proposal.ErrorText);
            MysqlCommandHelper.Add(cmd, "@decided_by_user_id", proposal.DecidedByUserId);
            MysqlCommandHelper.AddDate(cmd, "@decided_utc", proposal.DecidedUtc);
            MysqlCommandHelper.AddDate(cmd, "@executed_utc", proposal.ExecutedUtc);
            MysqlCommandHelper.AddDate(cmd, "@created_utc", proposal.CreatedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", proposal.LastUpdateUtc);
        }

        private static AskActionProposal FromReader(MySqlDataReader reader)
        {
            AskActionProposal proposal = new AskActionProposal();
            proposal.Id = reader["id"].ToString()!;
            proposal.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            proposal.UserId = MysqlCommandHelper.ReadString(reader["user_id"]);
            proposal.ThreadId = reader["thread_id"].ToString()!;
            proposal.MessageId = MysqlCommandHelper.ReadString(reader["message_id"]);
            proposal.ToolName = reader["tool_name"].ToString()!;
            proposal.ArgumentsText = reader["arguments_text"]?.ToString() ?? "{}";
            proposal.SummaryText = reader["summary_text"]?.ToString() ?? String.Empty;
            proposal.Source = MysqlCommandHelper.ReadEnum(reader["source"], AskProposalSourceEnum.Captain);
            proposal.Status = MysqlCommandHelper.ReadEnum(reader["status"], AskProposalStatusEnum.Pending);
            proposal.ResultText = MysqlCommandHelper.ReadString(reader["result_text"]);
            proposal.ErrorText = MysqlCommandHelper.ReadString(reader["error_text"]);
            proposal.DecidedByUserId = MysqlCommandHelper.ReadString(reader["decided_by_user_id"]);
            proposal.DecidedUtc = MysqlCommandHelper.ReadNullableDate(reader["decided_utc"]);
            proposal.ExecutedUtc = MysqlCommandHelper.ReadNullableDate(reader["executed_utc"]);
            proposal.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            proposal.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return proposal;
        }

        #endregion
    }
}
