namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQL Server implementation of fleet action run persistence. Deleting a run also deletes its targets.
    /// </summary>
    public class FleetActionRunMethods : IFleetActionRunMethods
    {
        #region Private-Members

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
        public FleetActionRunMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<FleetActionRun> CreateAsync(FleetActionRun run, CancellationToken token = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (String.IsNullOrEmpty(run.Id)) run.Id = Constants.IdGenerator.GenerateKSortable(Constants.FleetActionRunIdPrefix, 24);
            run.LastUpdateUtc = DateTime.UtcNow;

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, @"INSERT INTO fleet_action_runs
                    (id, tenant_id, user_id, action_id, action_name, kind, command_text, prompt_template, pipeline_id, persona, timeout_seconds, requires_clean_working_tree, concurrency, status, target_count, succeeded_count, failed_count, skipped_count, cancelled_count, started_utc, completed_utc, created_utc, last_update_utc)
                    VALUES
                    (@id, @tenant_id, @user_id, @action_id, @action_name, @kind, @command_text, @prompt_template, @pipeline_id, @persona, @timeout_seconds, @requires_clean_working_tree, @concurrency, @status, @target_count, @succeeded_count, @failed_count, @skipped_count, @cancelled_count, @started_utc, @completed_utc, @created_utc, @last_update_utc);",
                    cmd => Bind(cmd, run), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return run;
        }

        /// <inheritdoc />
        public async Task<FleetActionRun?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<FleetActionRun> results = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_action_runs WHERE id = @id;",
                cmd => SqlServerCommandHelper.Add(cmd, "@id", id),
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<FleetActionRun?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<FleetActionRun> results = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_action_runs WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@id", id);
                },
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<FleetActionRun> UpdateAsync(FleetActionRun run, CancellationToken token = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (String.IsNullOrEmpty(run.Id)) throw new ArgumentException("Run identifier is required.", nameof(run));
            run.LastUpdateUtc = DateTime.UtcNow;

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, @"UPDATE fleet_action_runs SET
                    tenant_id = @tenant_id, user_id = @user_id, action_id = @action_id, action_name = @action_name,
                    kind = @kind, command_text = @command_text, prompt_template = @prompt_template,
                    pipeline_id = @pipeline_id, persona = @persona, timeout_seconds = @timeout_seconds,
                    requires_clean_working_tree = @requires_clean_working_tree, concurrency = @concurrency,
                    status = @status, target_count = @target_count, succeeded_count = @succeeded_count,
                    failed_count = @failed_count, skipped_count = @skipped_count, cancelled_count = @cancelled_count,
                    started_utc = @started_utc, completed_utc = @completed_utc, created_utc = @created_utc,
                    last_update_utc = @last_update_utc
                    WHERE id = @id;", cmd => Bind(cmd, run), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return run;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM fleet_action_run_targets WHERE run_id = @id;", cmd => SqlServerCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM fleet_action_runs WHERE id = @id;", cmd => SqlServerCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                Action<SqlCommand> bind = cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@id", id);
                };
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM fleet_action_run_targets WHERE tenant_id = @tenant_id AND run_id = @id;", bind, token).ConfigureAwait(false);
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM fleet_action_runs WHERE tenant_id = @tenant_id AND id = @id;", bind, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<FleetActionRun>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) query = new EnumerationQuery();

            List<string> conditions = new List<string> { "tenant_id = @tenant_id" };
            if (query.CreatedAfter.HasValue) conditions.Add("created_utc > @created_after");
            if (query.CreatedBefore.HasValue) conditions.Add("created_utc < @created_before");
            if (!String.IsNullOrEmpty(query.Status)) conditions.Add("status = @status");

            Action<SqlCommand> bind = cmd =>
            {
                SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                if (query.CreatedAfter.HasValue) SqlServerCommandHelper.AddDate(cmd, "@created_after", query.CreatedAfter.Value);
                if (query.CreatedBefore.HasValue) SqlServerCommandHelper.AddDate(cmd, "@created_before", query.CreatedBefore.Value);
                if (!String.IsNullOrEmpty(query.Status)) SqlServerCommandHelper.Add(cmd, "@status", NormalizeStatus(query.Status!));
            };

            string where = " WHERE " + String.Join(" AND ", conditions);
            string direction = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqlServerCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM fleet_action_runs" + where + ";", bind, token).ConfigureAwait(false);
                List<FleetActionRun> rows = await SqlServerCommandHelper.QueryAsync(conn,
                    "SELECT * FROM fleet_action_runs" + where + " ORDER BY created_utc " + direction + ", id " + direction + SqlServerCommandHelper.Page(query.Offset, query.PageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);
                return EnumerationResult<FleetActionRun>.Create(query, rows, total);
            }
        }

        /// <inheritdoc />
        public async Task<List<FleetActionRun>> EnumerateUnfinishedAsync(CancellationToken token = default)
        {
            return await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_action_runs WHERE status IN (@pending, @running) ORDER BY created_utc ASC, id ASC;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@pending", FleetActionRunStatusEnum.Pending.ToString());
                    SqlServerCommandHelper.Add(cmd, "@running", FleetActionRunStatusEnum.Running.ToString());
                },
                FromReader, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static string NormalizeStatus(string status)
        {
            return Enum.TryParse<FleetActionRunStatusEnum>(status, true, out FleetActionRunStatusEnum parsed) ? parsed.ToString() : status;
        }

        private static void Bind(SqlCommand cmd, FleetActionRun run)
        {
            SqlServerCommandHelper.Add(cmd, "@id", run.Id);
            SqlServerCommandHelper.Add(cmd, "@tenant_id", run.TenantId);
            SqlServerCommandHelper.Add(cmd, "@user_id", run.UserId);
            SqlServerCommandHelper.Add(cmd, "@action_id", run.ActionId);
            SqlServerCommandHelper.Add(cmd, "@action_name", run.ActionName);
            SqlServerCommandHelper.Add(cmd, "@kind", run.Kind.ToString());
            SqlServerCommandHelper.Add(cmd, "@command_text", run.CommandText);
            SqlServerCommandHelper.Add(cmd, "@prompt_template", run.PromptTemplate);
            SqlServerCommandHelper.Add(cmd, "@pipeline_id", run.PipelineId);
            SqlServerCommandHelper.Add(cmd, "@persona", run.Persona);
            SqlServerCommandHelper.Add(cmd, "@timeout_seconds", run.TimeoutSeconds);
            SqlServerCommandHelper.Add(cmd, "@requires_clean_working_tree", run.RequiresCleanWorkingTree);
            SqlServerCommandHelper.Add(cmd, "@concurrency", run.Concurrency);
            SqlServerCommandHelper.Add(cmd, "@status", run.Status.ToString());
            SqlServerCommandHelper.Add(cmd, "@target_count", run.TargetCount);
            SqlServerCommandHelper.Add(cmd, "@succeeded_count", run.SucceededCount);
            SqlServerCommandHelper.Add(cmd, "@failed_count", run.FailedCount);
            SqlServerCommandHelper.Add(cmd, "@skipped_count", run.SkippedCount);
            SqlServerCommandHelper.Add(cmd, "@cancelled_count", run.CancelledCount);
            SqlServerCommandHelper.AddDate(cmd, "@started_utc", run.StartedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@completed_utc", run.CompletedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@created_utc", run.CreatedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", run.LastUpdateUtc);
        }

        private static FleetActionRun FromReader(SqlDataReader reader)
        {
            FleetActionRun run = new FleetActionRun();
            run.Id = reader["id"].ToString()!;
            run.TenantId = SqlServerCommandHelper.ReadString(reader["tenant_id"]);
            run.UserId = SqlServerCommandHelper.ReadString(reader["user_id"]);
            run.ActionId = SqlServerCommandHelper.ReadString(reader["action_id"]);
            run.ActionName = SqlServerCommandHelper.ReadString(reader["action_name"]) ?? String.Empty;
            run.Kind = SqlServerCommandHelper.ReadEnum(reader["kind"], FleetActionKindEnum.Command);
            run.CommandText = SqlServerCommandHelper.ReadString(reader["command_text"]);
            run.PromptTemplate = SqlServerCommandHelper.ReadString(reader["prompt_template"]);
            run.PipelineId = SqlServerCommandHelper.ReadString(reader["pipeline_id"]);
            run.Persona = SqlServerCommandHelper.ReadString(reader["persona"]);
            run.TimeoutSeconds = SqlServerCommandHelper.ReadInt(reader["timeout_seconds"], 300);
            run.RequiresCleanWorkingTree = SqlServerCommandHelper.ReadBool(reader["requires_clean_working_tree"], true);
            run.Concurrency = SqlServerCommandHelper.ReadInt(reader["concurrency"], 4);
            run.Status = SqlServerCommandHelper.ReadEnum(reader["status"], FleetActionRunStatusEnum.Pending);
            run.TargetCount = SqlServerCommandHelper.ReadInt(reader["target_count"], 0);
            run.SucceededCount = SqlServerCommandHelper.ReadInt(reader["succeeded_count"], 0);
            run.FailedCount = SqlServerCommandHelper.ReadInt(reader["failed_count"], 0);
            run.SkippedCount = SqlServerCommandHelper.ReadInt(reader["skipped_count"], 0);
            run.CancelledCount = SqlServerCommandHelper.ReadInt(reader["cancelled_count"], 0);
            run.StartedUtc = SqlServerCommandHelper.ReadNullableDate(reader["started_utc"]);
            run.CompletedUtc = SqlServerCommandHelper.ReadNullableDate(reader["completed_utc"]);
            run.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            run.LastUpdateUtc = SqlServerCommandHelper.ReadDate(reader["last_update_utc"]);
            return run;
        }

        #endregion
    }
}
