namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of fleet action run persistence. Deleting a run also deletes its targets.
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
        /// <param name="connectionString">MySQL connection string.</param>
        public FleetActionRunMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
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

            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, @"INSERT INTO fleet_action_runs
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
            List<FleetActionRun> results = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_action_runs WHERE id = @id;",
                cmd => MysqlCommandHelper.Add(cmd, "@id", id),
                FromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task<FleetActionRun?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<FleetActionRun> results = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_action_runs WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@id", id);
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

            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, @"UPDATE fleet_action_runs SET
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
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM fleet_action_run_targets WHERE run_id = @id;", cmd => MysqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM fleet_action_runs WHERE id = @id;", cmd => MysqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                Action<MySqlCommand> bind = cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@id", id);
                };
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM fleet_action_run_targets WHERE tenant_id = @tenant_id AND run_id = @id;", bind, token).ConfigureAwait(false);
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM fleet_action_runs WHERE tenant_id = @tenant_id AND id = @id;", bind, token).ConfigureAwait(false);
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

            Action<MySqlCommand> bind = cmd =>
            {
                MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                if (query.CreatedAfter.HasValue) MysqlCommandHelper.AddDate(cmd, "@created_after", query.CreatedAfter.Value);
                if (query.CreatedBefore.HasValue) MysqlCommandHelper.AddDate(cmd, "@created_before", query.CreatedBefore.Value);
                if (!String.IsNullOrEmpty(query.Status)) MysqlCommandHelper.Add(cmd, "@status", NormalizeStatus(query.Status!));
            };

            string where = " WHERE " + String.Join(" AND ", conditions);
            string direction = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM fleet_action_runs" + where + ";", bind, token).ConfigureAwait(false);
                List<FleetActionRun> rows = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM fleet_action_runs" + where + " ORDER BY created_utc " + direction + ", id " + direction + MysqlCommandHelper.Page(query.Offset, query.PageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);
                return EnumerationResult<FleetActionRun>.Create(query, rows, total);
            }
        }

        /// <inheritdoc />
        public async Task<List<FleetActionRun>> EnumerateUnfinishedAsync(CancellationToken token = default)
        {
            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM fleet_action_runs WHERE status IN (@pending, @running) ORDER BY created_utc ASC, id ASC;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@pending", FleetActionRunStatusEnum.Pending.ToString());
                    MysqlCommandHelper.Add(cmd, "@running", FleetActionRunStatusEnum.Running.ToString());
                },
                FromReader, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static string NormalizeStatus(string status)
        {
            return Enum.TryParse<FleetActionRunStatusEnum>(status, true, out FleetActionRunStatusEnum parsed) ? parsed.ToString() : status;
        }

        private static void Bind(MySqlCommand cmd, FleetActionRun run)
        {
            MysqlCommandHelper.Add(cmd, "@id", run.Id);
            MysqlCommandHelper.Add(cmd, "@tenant_id", run.TenantId);
            MysqlCommandHelper.Add(cmd, "@user_id", run.UserId);
            MysqlCommandHelper.Add(cmd, "@action_id", run.ActionId);
            MysqlCommandHelper.Add(cmd, "@action_name", run.ActionName);
            MysqlCommandHelper.Add(cmd, "@kind", run.Kind.ToString());
            MysqlCommandHelper.Add(cmd, "@command_text", run.CommandText);
            MysqlCommandHelper.Add(cmd, "@prompt_template", run.PromptTemplate);
            MysqlCommandHelper.Add(cmd, "@pipeline_id", run.PipelineId);
            MysqlCommandHelper.Add(cmd, "@persona", run.Persona);
            MysqlCommandHelper.Add(cmd, "@timeout_seconds", run.TimeoutSeconds);
            MysqlCommandHelper.Add(cmd, "@requires_clean_working_tree", run.RequiresCleanWorkingTree);
            MysqlCommandHelper.Add(cmd, "@concurrency", run.Concurrency);
            MysqlCommandHelper.Add(cmd, "@status", run.Status.ToString());
            MysqlCommandHelper.Add(cmd, "@target_count", run.TargetCount);
            MysqlCommandHelper.Add(cmd, "@succeeded_count", run.SucceededCount);
            MysqlCommandHelper.Add(cmd, "@failed_count", run.FailedCount);
            MysqlCommandHelper.Add(cmd, "@skipped_count", run.SkippedCount);
            MysqlCommandHelper.Add(cmd, "@cancelled_count", run.CancelledCount);
            MysqlCommandHelper.AddDate(cmd, "@started_utc", run.StartedUtc);
            MysqlCommandHelper.AddDate(cmd, "@completed_utc", run.CompletedUtc);
            MysqlCommandHelper.AddDate(cmd, "@created_utc", run.CreatedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", run.LastUpdateUtc);
        }

        private static FleetActionRun FromReader(MySqlDataReader reader)
        {
            FleetActionRun run = new FleetActionRun();
            run.Id = reader["id"].ToString()!;
            run.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            run.UserId = MysqlCommandHelper.ReadString(reader["user_id"]);
            run.ActionId = MysqlCommandHelper.ReadString(reader["action_id"]);
            run.ActionName = MysqlCommandHelper.ReadString(reader["action_name"]) ?? String.Empty;
            run.Kind = MysqlCommandHelper.ReadEnum(reader["kind"], FleetActionKindEnum.Command);
            run.CommandText = MysqlCommandHelper.ReadString(reader["command_text"]);
            run.PromptTemplate = MysqlCommandHelper.ReadString(reader["prompt_template"]);
            run.PipelineId = MysqlCommandHelper.ReadString(reader["pipeline_id"]);
            run.Persona = MysqlCommandHelper.ReadString(reader["persona"]);
            run.TimeoutSeconds = MysqlCommandHelper.ReadInt(reader["timeout_seconds"], 300);
            run.RequiresCleanWorkingTree = MysqlCommandHelper.ReadBool(reader["requires_clean_working_tree"], true);
            run.Concurrency = MysqlCommandHelper.ReadInt(reader["concurrency"], 4);
            run.Status = MysqlCommandHelper.ReadEnum(reader["status"], FleetActionRunStatusEnum.Pending);
            run.TargetCount = MysqlCommandHelper.ReadInt(reader["target_count"], 0);
            run.SucceededCount = MysqlCommandHelper.ReadInt(reader["succeeded_count"], 0);
            run.FailedCount = MysqlCommandHelper.ReadInt(reader["failed_count"], 0);
            run.SkippedCount = MysqlCommandHelper.ReadInt(reader["skipped_count"], 0);
            run.CancelledCount = MysqlCommandHelper.ReadInt(reader["cancelled_count"], 0);
            run.StartedUtc = MysqlCommandHelper.ReadNullableDate(reader["started_utc"]);
            run.CompletedUtc = MysqlCommandHelper.ReadNullableDate(reader["completed_utc"]);
            run.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            run.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return run;
        }

        #endregion
    }
}
