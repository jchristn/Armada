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
    /// SQL Server implementation of Ask Armada action proposal persistence.
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
        /// <param name="driver">SQL Server database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public AskActionProposalMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<AskActionProposal> CreateAsync(AskActionProposal proposal, CancellationToken token = default)
        {
            if (proposal == null) throw new ArgumentNullException(nameof(proposal));
            if (String.IsNullOrEmpty(proposal.TenantId)) throw new ArgumentException("TenantId is required.", nameof(proposal));
            if (String.IsNullOrEmpty(proposal.ThreadId)) throw new ArgumentException("ThreadId is required.", nameof(proposal));

            proposal.LastUpdateUtc = DateTime.UtcNow;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, proposal), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return proposal;
        }

        /// <inheritdoc />
        public async Task<AskActionProposal?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            List<AskActionProposal> rows = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_action_proposals WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@id", id);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<AskActionProposal> UpdateAsync(AskActionProposal proposal, CancellationToken token = default)
        {
            if (proposal == null) throw new ArgumentNullException(nameof(proposal));
            if (String.IsNullOrEmpty(proposal.TenantId)) throw new ArgumentException("TenantId is required.", nameof(proposal));

            proposal.LastUpdateUtc = DateTime.UtcNow;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, _Update, cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@message_id", proposal.MessageId);
                    SqlServerCommandHelper.Add(cmd, "@summary_text", proposal.SummaryText);
                    SqlServerCommandHelper.Add(cmd, "@status", proposal.Status.ToString());
                    SqlServerCommandHelper.Add(cmd, "@result_text", proposal.ResultText);
                    SqlServerCommandHelper.Add(cmd, "@error_text", proposal.ErrorText);
                    SqlServerCommandHelper.Add(cmd, "@decided_by_user_id", proposal.DecidedByUserId);
                    SqlServerCommandHelper.AddDate(cmd, "@decided_utc", proposal.DecidedUtc);
                    SqlServerCommandHelper.AddDate(cmd, "@executed_utc", proposal.ExecutedUtc);
                    SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", proposal.LastUpdateUtc);
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", proposal.TenantId);
                    SqlServerCommandHelper.Add(cmd, "@id", proposal.Id);
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
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                updated = await SqlServerCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE ask_action_proposals SET status = @to, decided_by_user_id = @decided_by_user_id, decided_utc = @now, last_update_utc = @now WHERE tenant_id = @tenant_id AND id = @id AND status = @from;",
                    cmd =>
                    {
                        SqlServerCommandHelper.Add(cmd, "@to", to.ToString());
                        SqlServerCommandHelper.Add(cmd, "@decided_by_user_id", decidedByUserId);
                        SqlServerCommandHelper.AddDate(cmd, "@now", now);
                        SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                        SqlServerCommandHelper.Add(cmd, "@id", id);
                        SqlServerCommandHelper.Add(cmd, "@from", from.ToString());
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return updated == 1;
        }

        /// <inheritdoc />
        public async Task<List<AskActionProposal>> EnumerateByThreadAsync(string tenantId, string threadId, AskProposalStatusEnum? status, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(threadId)) throw new ArgumentNullException(nameof(threadId));

            return await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_action_proposals WHERE tenant_id = @tenant_id AND thread_id = @thread_id" + (status.HasValue ? " AND status = @status" : String.Empty) + " ORDER BY created_utc ASC, id ASC;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@thread_id", threadId);
                    if (status.HasValue) SqlServerCommandHelper.Add(cmd, "@status", status.Value.ToString());
                }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<AskActionProposal>> EnumeratePendingBeforeAsync(DateTime cutoffUtc, CancellationToken token = default)
        {
            return await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_action_proposals WHERE status = @status AND created_utc < @cutoff ORDER BY created_utc ASC, id ASC;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@status", AskProposalStatusEnum.Pending.ToString());
                    SqlServerCommandHelper.AddDate(cmd, "@cutoff", cutoffUtc);
                }, FromReader, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqlCommand cmd, AskActionProposal proposal)
        {
            SqlServerCommandHelper.Add(cmd, "@id", proposal.Id);
            SqlServerCommandHelper.Add(cmd, "@tenant_id", proposal.TenantId);
            SqlServerCommandHelper.Add(cmd, "@user_id", proposal.UserId);
            SqlServerCommandHelper.Add(cmd, "@thread_id", proposal.ThreadId);
            SqlServerCommandHelper.Add(cmd, "@message_id", proposal.MessageId);
            SqlServerCommandHelper.Add(cmd, "@tool_name", proposal.ToolName);
            SqlServerCommandHelper.Add(cmd, "@arguments_text", proposal.ArgumentsText);
            SqlServerCommandHelper.Add(cmd, "@summary_text", proposal.SummaryText);
            SqlServerCommandHelper.Add(cmd, "@source", proposal.Source.ToString());
            SqlServerCommandHelper.Add(cmd, "@status", proposal.Status.ToString());
            SqlServerCommandHelper.Add(cmd, "@result_text", proposal.ResultText);
            SqlServerCommandHelper.Add(cmd, "@error_text", proposal.ErrorText);
            SqlServerCommandHelper.Add(cmd, "@decided_by_user_id", proposal.DecidedByUserId);
            SqlServerCommandHelper.AddDate(cmd, "@decided_utc", proposal.DecidedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@executed_utc", proposal.ExecutedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@created_utc", proposal.CreatedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", proposal.LastUpdateUtc);
        }

        private static AskActionProposal FromReader(SqlDataReader reader)
        {
            AskActionProposal proposal = new AskActionProposal();
            proposal.Id = reader["id"].ToString()!;
            proposal.TenantId = SqlServerCommandHelper.ReadString(reader["tenant_id"]);
            proposal.UserId = SqlServerCommandHelper.ReadString(reader["user_id"]);
            proposal.ThreadId = reader["thread_id"].ToString()!;
            proposal.MessageId = SqlServerCommandHelper.ReadString(reader["message_id"]);
            proposal.ToolName = reader["tool_name"].ToString()!;
            proposal.ArgumentsText = reader["arguments_text"]?.ToString() ?? "{}";
            proposal.SummaryText = reader["summary_text"]?.ToString() ?? String.Empty;
            proposal.Source = SqlServerCommandHelper.ReadEnum(reader["source"], AskProposalSourceEnum.Captain);
            proposal.Status = SqlServerCommandHelper.ReadEnum(reader["status"], AskProposalStatusEnum.Pending);
            proposal.ResultText = SqlServerCommandHelper.ReadString(reader["result_text"]);
            proposal.ErrorText = SqlServerCommandHelper.ReadString(reader["error_text"]);
            proposal.DecidedByUserId = SqlServerCommandHelper.ReadString(reader["decided_by_user_id"]);
            proposal.DecidedUtc = SqlServerCommandHelper.ReadNullableDate(reader["decided_utc"]);
            proposal.ExecutedUtc = SqlServerCommandHelper.ReadNullableDate(reader["executed_utc"]);
            proposal.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            proposal.LastUpdateUtc = SqlServerCommandHelper.ReadDate(reader["last_update_utc"]);
            return proposal;
        }

        #endregion
    }
}
