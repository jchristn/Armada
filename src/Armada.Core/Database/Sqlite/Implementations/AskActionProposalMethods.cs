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
    /// SQLite implementation of Ask Armada action proposal persistence.
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
        /// <param name="driver">SQLite database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public AskActionProposalMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<AskActionProposal> CreateAsync(AskActionProposal proposal, CancellationToken token = default)
        {
            if (proposal == null) throw new ArgumentNullException(nameof(proposal));
            if (String.IsNullOrEmpty(proposal.TenantId)) throw new ArgumentException("TenantId is required.", nameof(proposal));
            if (String.IsNullOrEmpty(proposal.ThreadId)) throw new ArgumentException("ThreadId is required.", nameof(proposal));

            proposal.LastUpdateUtc = DateTime.UtcNow;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, proposal), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return proposal;
        }

        /// <inheritdoc />
        public async Task<AskActionProposal?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            List<AskActionProposal> rows = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_action_proposals WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqliteCommandHelper.Add(cmd, "@id", id);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<AskActionProposal> UpdateAsync(AskActionProposal proposal, CancellationToken token = default)
        {
            if (proposal == null) throw new ArgumentNullException(nameof(proposal));
            if (String.IsNullOrEmpty(proposal.TenantId)) throw new ArgumentException("TenantId is required.", nameof(proposal));

            proposal.LastUpdateUtc = DateTime.UtcNow;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, _Update, cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@message_id", proposal.MessageId);
                    SqliteCommandHelper.Add(cmd, "@summary_text", proposal.SummaryText);
                    SqliteCommandHelper.Add(cmd, "@status", proposal.Status.ToString());
                    SqliteCommandHelper.Add(cmd, "@result_text", proposal.ResultText);
                    SqliteCommandHelper.Add(cmd, "@error_text", proposal.ErrorText);
                    SqliteCommandHelper.Add(cmd, "@decided_by_user_id", proposal.DecidedByUserId);
                    SqliteCommandHelper.AddDate(cmd, "@decided_utc", proposal.DecidedUtc);
                    SqliteCommandHelper.AddDate(cmd, "@executed_utc", proposal.ExecutedUtc);
                    SqliteCommandHelper.AddDate(cmd, "@last_update_utc", proposal.LastUpdateUtc);
                    SqliteCommandHelper.Add(cmd, "@tenant_id", proposal.TenantId);
                    SqliteCommandHelper.Add(cmd, "@id", proposal.Id);
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
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                updated = await SqliteCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE ask_action_proposals SET status = @to, decided_by_user_id = @decided_by_user_id, decided_utc = @now, last_update_utc = @now WHERE tenant_id = @tenant_id AND id = @id AND status = @from;",
                    cmd =>
                    {
                        SqliteCommandHelper.Add(cmd, "@to", to.ToString());
                        SqliteCommandHelper.Add(cmd, "@decided_by_user_id", decidedByUserId);
                        SqliteCommandHelper.AddDate(cmd, "@now", now);
                        SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                        SqliteCommandHelper.Add(cmd, "@id", id);
                        SqliteCommandHelper.Add(cmd, "@from", from.ToString());
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return updated == 1;
        }

        /// <inheritdoc />
        public async Task<List<AskActionProposal>> EnumerateByThreadAsync(string tenantId, string threadId, AskProposalStatusEnum? status, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(threadId)) throw new ArgumentNullException(nameof(threadId));

            return await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_action_proposals WHERE tenant_id = @tenant_id AND thread_id = @thread_id" + (status.HasValue ? " AND status = @status" : String.Empty) + " ORDER BY created_utc ASC, id ASC;",
                cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqliteCommandHelper.Add(cmd, "@thread_id", threadId);
                    if (status.HasValue) SqliteCommandHelper.Add(cmd, "@status", status.Value.ToString());
                }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<AskActionProposal>> EnumeratePendingBeforeAsync(DateTime cutoffUtc, CancellationToken token = default)
        {
            return await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM ask_action_proposals WHERE status = @status AND created_utc < @cutoff ORDER BY created_utc ASC, id ASC;",
                cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@status", AskProposalStatusEnum.Pending.ToString());
                    SqliteCommandHelper.AddDate(cmd, "@cutoff", cutoffUtc);
                }, FromReader, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqliteCommand cmd, AskActionProposal proposal)
        {
            SqliteCommandHelper.Add(cmd, "@id", proposal.Id);
            SqliteCommandHelper.Add(cmd, "@tenant_id", proposal.TenantId);
            SqliteCommandHelper.Add(cmd, "@user_id", proposal.UserId);
            SqliteCommandHelper.Add(cmd, "@thread_id", proposal.ThreadId);
            SqliteCommandHelper.Add(cmd, "@message_id", proposal.MessageId);
            SqliteCommandHelper.Add(cmd, "@tool_name", proposal.ToolName);
            SqliteCommandHelper.Add(cmd, "@arguments_text", proposal.ArgumentsText);
            SqliteCommandHelper.Add(cmd, "@summary_text", proposal.SummaryText);
            SqliteCommandHelper.Add(cmd, "@source", proposal.Source.ToString());
            SqliteCommandHelper.Add(cmd, "@status", proposal.Status.ToString());
            SqliteCommandHelper.Add(cmd, "@result_text", proposal.ResultText);
            SqliteCommandHelper.Add(cmd, "@error_text", proposal.ErrorText);
            SqliteCommandHelper.Add(cmd, "@decided_by_user_id", proposal.DecidedByUserId);
            SqliteCommandHelper.AddDate(cmd, "@decided_utc", proposal.DecidedUtc);
            SqliteCommandHelper.AddDate(cmd, "@executed_utc", proposal.ExecutedUtc);
            SqliteCommandHelper.AddDate(cmd, "@created_utc", proposal.CreatedUtc);
            SqliteCommandHelper.AddDate(cmd, "@last_update_utc", proposal.LastUpdateUtc);
        }

        private static AskActionProposal FromReader(SqliteDataReader reader)
        {
            AskActionProposal proposal = new AskActionProposal();
            proposal.Id = reader["id"].ToString()!;
            proposal.TenantId = SqliteCommandHelper.ReadString(reader["tenant_id"]);
            proposal.UserId = SqliteCommandHelper.ReadString(reader["user_id"]);
            proposal.ThreadId = reader["thread_id"].ToString()!;
            proposal.MessageId = SqliteCommandHelper.ReadString(reader["message_id"]);
            proposal.ToolName = reader["tool_name"].ToString()!;
            proposal.ArgumentsText = reader["arguments_text"]?.ToString() ?? "{}";
            proposal.SummaryText = reader["summary_text"]?.ToString() ?? String.Empty;
            proposal.Source = SqliteCommandHelper.ReadEnum(reader["source"], AskProposalSourceEnum.Captain);
            proposal.Status = SqliteCommandHelper.ReadEnum(reader["status"], AskProposalStatusEnum.Pending);
            proposal.ResultText = SqliteCommandHelper.ReadString(reader["result_text"]);
            proposal.ErrorText = SqliteCommandHelper.ReadString(reader["error_text"]);
            proposal.DecidedByUserId = SqliteCommandHelper.ReadString(reader["decided_by_user_id"]);
            proposal.DecidedUtc = SqliteCommandHelper.ReadNullableDate(reader["decided_utc"]);
            proposal.ExecutedUtc = SqliteCommandHelper.ReadNullableDate(reader["executed_utc"]);
            proposal.CreatedUtc = SqliteCommandHelper.ReadDate(reader["created_utc"]);
            proposal.LastUpdateUtc = SqliteCommandHelper.ReadDate(reader["last_update_utc"]);
            return proposal;
        }

        #endregion
    }
}
