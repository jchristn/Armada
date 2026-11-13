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
    /// MySQL implementation of model probe run persistence.
    /// </summary>
    public class ModelProbeRunMethods : IModelProbeRunMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO model_probe_runs
            (id, tenant_id, user_id, target_kind, captain_id, captain_name, model_endpoint_id, depth, status, is_evaluation,
             probe_set_version, runtime, runtime_version, model_provider, model, model_digest, configured_context_tokens,
             served_context_tokens, harbor_id, inference_host, token_budget, tokens_used, passed_count, failed_count,
             could_not_run_count, failure_reason, battery_run_id, report_json, created_utc, started_utc, completed_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @user_id, @target_kind, @captain_id, @captain_name, @model_endpoint_id, @depth, @status, @is_evaluation,
             @probe_set_version, @runtime, @runtime_version, @model_provider, @model, @model_digest, @configured_context_tokens,
             @served_context_tokens, @harbor_id, @inference_host, @token_budget, @tokens_used, @passed_count, @failed_count,
             @could_not_run_count, @failure_reason, @battery_run_id, @report_json, @created_utc, @started_utc, @completed_utc, @last_update_utc);";

        private static readonly string _Update = @"UPDATE model_probe_runs SET
            target_kind = @target_kind, captain_id = @captain_id, captain_name = @captain_name, model_endpoint_id = @model_endpoint_id,
            depth = @depth, status = @status, is_evaluation = @is_evaluation, probe_set_version = @probe_set_version,
            runtime = @runtime, runtime_version = @runtime_version, model_provider = @model_provider, model = @model,
            model_digest = @model_digest, configured_context_tokens = @configured_context_tokens,
            served_context_tokens = @served_context_tokens, harbor_id = @harbor_id, inference_host = @inference_host,
            token_budget = @token_budget, tokens_used = @tokens_used, passed_count = @passed_count, failed_count = @failed_count,
            could_not_run_count = @could_not_run_count, failure_reason = @failure_reason, battery_run_id = @battery_run_id, report_json = @report_json, started_utc = @started_utc,
            completed_utc = @completed_utc, last_update_utc = @last_update_utc
            WHERE id = @id;";

        private readonly string _ConnectionString;
        private readonly SemaphoreSlim? _WriteLock;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when connectionString is null.</exception>
        public ModelProbeRunMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<ModelProbeRun> CreateAsync(ModelProbeRun run, CancellationToken token = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            run.LastUpdateUtc = DateTime.UtcNow;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, run, true), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return run;
        }

        /// <inheritdoc />
        public async Task<ModelProbeRun?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<ModelProbeRun> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM model_probe_runs WHERE id = @id;",
                cmd => MysqlCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<ModelProbeRun> UpdateAsync(ModelProbeRun run, CancellationToken token = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            run.LastUpdateUtc = DateTime.UtcNow;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Update, cmd => Bind(cmd, run, false), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return run;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int deleted = 0;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM model_probe_results WHERE run_id = @id;",
                    cmd => MysqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM model_probe_runs WHERE id = @id;",
                    cmd => MysqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted > 0;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<ModelProbeRun>> EnumerateAsync(ModelProbeRunQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            int pageSize = query.PageSize < 1 ? 1 : (query.PageSize > 1000 ? 1000 : query.PageSize);
            int pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            int offset = (pageNumber - 1) * pageSize;

            List<string> conditions = new List<string>();
            if (query.TenantId != null) conditions.Add("tenant_id = @tenant_id");
            if (query.CaptainId != null) conditions.Add("captain_id = @captain_id");
            if (query.ModelEndpointId != null) conditions.Add("model_endpoint_id = @model_endpoint_id");
            if (query.Status.HasValue) conditions.Add("status = @status");
            if (query.FromUtc.HasValue) conditions.Add("created_utc >= @from_utc");

            Action<MySqlCommand> bind = cmd =>
            {
                if (query.TenantId != null) MysqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.CaptainId != null) MysqlCommandHelper.Add(cmd, "@captain_id", query.CaptainId);
                if (query.ModelEndpointId != null) MysqlCommandHelper.Add(cmd, "@model_endpoint_id", query.ModelEndpointId);
                if (query.Status.HasValue) MysqlCommandHelper.Add(cmd, "@status", query.Status.Value.ToString());
                if (query.FromUtc.HasValue) MysqlCommandHelper.AddDate(cmd, "@from_utc", query.FromUtc.Value);
            };

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM model_probe_runs" + where + ";", bind, token).ConfigureAwait(false);
                List<ModelProbeRun> rows = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM model_probe_runs" + where + " ORDER BY created_utc DESC, id DESC" + MysqlCommandHelper.Page(offset, pageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);

                EnumerationResult<ModelProbeRun> result = new EnumerationResult<ModelProbeRun>();
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalRecords = total;
                result.TotalPages = (int)Math.Ceiling((double)total / pageSize);
                result.Objects = rows;
                return result;
            }
        }

        /// <inheritdoc />
        public async Task<long> SumTokensUsedSinceAsync(DateTime sinceUtc, string? inferenceHost, CancellationToken token = default)
        {
            string sql = "SELECT COALESCE(SUM(tokens_used), 0) FROM model_probe_runs WHERE created_utc >= @since_utc"
                + (inferenceHost != null ? " AND inference_host = @inference_host" : String.Empty) + ";";
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                return await MysqlCommandHelper.CountAsync(conn, sql, cmd =>
                {
                    MysqlCommandHelper.AddDate(cmd, "@since_utc", sinceUtc);
                    if (inferenceHost != null) MysqlCommandHelper.Add(cmd, "@inference_host", inferenceHost);
                }, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static void Bind(MySqlCommand cmd, ModelProbeRun run, bool includeCreateColumns)
        {
            MysqlCommandHelper.Add(cmd, "@id", run.Id);
            if (includeCreateColumns)
            {
                MysqlCommandHelper.Add(cmd, "@tenant_id", run.TenantId);
                MysqlCommandHelper.Add(cmd, "@user_id", run.UserId);
                MysqlCommandHelper.AddDate(cmd, "@created_utc", run.CreatedUtc);
            }

            MysqlCommandHelper.Add(cmd, "@target_kind", run.TargetKind.ToString());
            MysqlCommandHelper.Add(cmd, "@captain_id", run.CaptainId);
            MysqlCommandHelper.Add(cmd, "@captain_name", run.CaptainName);
            MysqlCommandHelper.Add(cmd, "@model_endpoint_id", run.ModelEndpointId);
            MysqlCommandHelper.Add(cmd, "@depth", run.Depth.ToString());
            MysqlCommandHelper.Add(cmd, "@status", run.Status.ToString());
            MysqlCommandHelper.Add(cmd, "@is_evaluation", run.IsEvaluation);
            MysqlCommandHelper.Add(cmd, "@probe_set_version", run.ProbeSetVersion ?? String.Empty);
            MysqlCommandHelper.Add(cmd, "@runtime", run.Runtime);
            MysqlCommandHelper.Add(cmd, "@runtime_version", run.RuntimeVersion);
            MysqlCommandHelper.Add(cmd, "@model_provider", run.ModelProvider);
            MysqlCommandHelper.Add(cmd, "@model", run.Model);
            MysqlCommandHelper.Add(cmd, "@model_digest", run.ModelDigest);
            MysqlCommandHelper.Add(cmd, "@configured_context_tokens", run.ConfiguredContextTokens);
            MysqlCommandHelper.Add(cmd, "@served_context_tokens", run.ServedContextTokens);
            MysqlCommandHelper.Add(cmd, "@harbor_id", run.HarborId);
            MysqlCommandHelper.Add(cmd, "@inference_host", run.InferenceHost);
            MysqlCommandHelper.Add(cmd, "@token_budget", run.TokenBudget);
            MysqlCommandHelper.Add(cmd, "@tokens_used", run.TokensUsed);
            MysqlCommandHelper.Add(cmd, "@passed_count", run.PassedCount);
            MysqlCommandHelper.Add(cmd, "@failed_count", run.FailedCount);
            MysqlCommandHelper.Add(cmd, "@could_not_run_count", run.CouldNotRunCount);
            MysqlCommandHelper.Add(cmd, "@failure_reason", run.FailureReason);
            MysqlCommandHelper.Add(cmd, "@battery_run_id", run.BatteryRunId);
            MysqlCommandHelper.Add(cmd, "@report_json", ModelEvaluationReport.ToJson(run.Report));
            MysqlCommandHelper.AddDate(cmd, "@started_utc", run.StartedUtc);
            MysqlCommandHelper.AddDate(cmd, "@completed_utc", run.CompletedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", run.LastUpdateUtc);
        }

        private static ModelProbeRun FromReader(MySqlDataReader reader)
        {
            ModelProbeRun run = new ModelProbeRun();
            run.Id = reader["id"].ToString()!;
            run.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            run.UserId = MysqlCommandHelper.ReadString(reader["user_id"]);
            run.TargetKind = MysqlCommandHelper.ReadEnum(reader["target_kind"], ModelProbeTargetEnum.Captain);
            run.CaptainId = MysqlCommandHelper.ReadString(reader["captain_id"]);
            run.CaptainName = MysqlCommandHelper.ReadString(reader["captain_name"]);
            run.ModelEndpointId = MysqlCommandHelper.ReadString(reader["model_endpoint_id"]);
            run.Depth = MysqlCommandHelper.ReadEnum(reader["depth"], ModelProbeDepthEnum.Quick);
            run.Status = MysqlCommandHelper.ReadEnum(reader["status"], ModelProbeRunStatusEnum.Queued);
            run.IsEvaluation = MysqlCommandHelper.ReadBool(reader["is_evaluation"], true);
            run.ProbeSetVersion = MysqlCommandHelper.ReadString(reader["probe_set_version"]) ?? String.Empty;
            run.Runtime = MysqlCommandHelper.ReadString(reader["runtime"]);
            run.RuntimeVersion = MysqlCommandHelper.ReadString(reader["runtime_version"]);
            run.ModelProvider = MysqlCommandHelper.ReadString(reader["model_provider"]);
            run.Model = MysqlCommandHelper.ReadString(reader["model"]);
            run.ModelDigest = MysqlCommandHelper.ReadString(reader["model_digest"]);
            run.ConfiguredContextTokens = MysqlCommandHelper.ReadNullableInt(reader["configured_context_tokens"]);
            run.ServedContextTokens = MysqlCommandHelper.ReadNullableInt(reader["served_context_tokens"]);
            run.HarborId = MysqlCommandHelper.ReadString(reader["harbor_id"]);
            run.InferenceHost = MysqlCommandHelper.ReadString(reader["inference_host"]);
            run.TokenBudget = MysqlCommandHelper.ReadNullableLong(reader["token_budget"]) ?? 0;
            run.TokensUsed = MysqlCommandHelper.ReadNullableLong(reader["tokens_used"]) ?? 0;
            run.PassedCount = MysqlCommandHelper.ReadInt(reader["passed_count"], 0);
            run.FailedCount = MysqlCommandHelper.ReadInt(reader["failed_count"], 0);
            run.CouldNotRunCount = MysqlCommandHelper.ReadInt(reader["could_not_run_count"], 0);
            run.FailureReason = MysqlCommandHelper.ReadString(reader["failure_reason"]);
            run.BatteryRunId = MysqlCommandHelper.ReadString(reader["battery_run_id"]);
            run.Report = ModelEvaluationReport.FromJson(MysqlCommandHelper.ReadString(reader["report_json"]));
            run.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            run.StartedUtc = MysqlCommandHelper.ReadNullableDate(reader["started_utc"]);
            run.CompletedUtc = MysqlCommandHelper.ReadNullableDate(reader["completed_utc"]);
            run.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return run;
        }

        #endregion
    }
}
