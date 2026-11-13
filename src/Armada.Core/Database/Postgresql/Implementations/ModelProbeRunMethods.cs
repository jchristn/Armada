namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// PostgreSQL implementation of model probe run persistence.
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
        /// <param name="driver">PostgreSQL database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public ModelProbeRunMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<ModelProbeRun> CreateAsync(ModelProbeRun run, CancellationToken token = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            run.LastUpdateUtc = DateTime.UtcNow;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, run, true), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return run;
        }

        /// <inheritdoc />
        public async Task<ModelProbeRun?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<ModelProbeRun> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM model_probe_runs WHERE id = @id;",
                cmd => PostgresqlCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<ModelProbeRun> UpdateAsync(ModelProbeRun run, CancellationToken token = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            run.LastUpdateUtc = DateTime.UtcNow;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Update, cmd => Bind(cmd, run, false), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return run;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int deleted = 0;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM model_probe_results WHERE run_id = @id;",
                    cmd => PostgresqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
                deleted = await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM model_probe_runs WHERE id = @id;",
                    cmd => PostgresqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
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

            Action<NpgsqlCommand> bind = cmd =>
            {
                if (query.TenantId != null) PostgresqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.CaptainId != null) PostgresqlCommandHelper.Add(cmd, "@captain_id", query.CaptainId);
                if (query.ModelEndpointId != null) PostgresqlCommandHelper.Add(cmd, "@model_endpoint_id", query.ModelEndpointId);
                if (query.Status.HasValue) PostgresqlCommandHelper.Add(cmd, "@status", query.Status.Value.ToString());
                if (query.FromUtc.HasValue) PostgresqlCommandHelper.AddDate(cmd, "@from_utc", query.FromUtc.Value);
            };

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (NpgsqlConnection conn = new NpgsqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await PostgresqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM model_probe_runs" + where + ";", bind, token).ConfigureAwait(false);
                List<ModelProbeRun> rows = await PostgresqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM model_probe_runs" + where + " ORDER BY created_utc DESC, id DESC" + PostgresqlCommandHelper.Page(offset, pageSize) + ";",
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
            using (NpgsqlConnection conn = new NpgsqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                return await PostgresqlCommandHelper.CountAsync(conn, sql, cmd =>
                {
                    PostgresqlCommandHelper.AddDate(cmd, "@since_utc", sinceUtc);
                    if (inferenceHost != null) PostgresqlCommandHelper.Add(cmd, "@inference_host", inferenceHost);
                }, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static void Bind(NpgsqlCommand cmd, ModelProbeRun run, bool includeCreateColumns)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", run.Id);
            if (includeCreateColumns)
            {
                PostgresqlCommandHelper.Add(cmd, "@tenant_id", run.TenantId);
                PostgresqlCommandHelper.Add(cmd, "@user_id", run.UserId);
                PostgresqlCommandHelper.AddDate(cmd, "@created_utc", run.CreatedUtc);
            }

            PostgresqlCommandHelper.Add(cmd, "@target_kind", run.TargetKind.ToString());
            PostgresqlCommandHelper.Add(cmd, "@captain_id", run.CaptainId);
            PostgresqlCommandHelper.Add(cmd, "@captain_name", run.CaptainName);
            PostgresqlCommandHelper.Add(cmd, "@model_endpoint_id", run.ModelEndpointId);
            PostgresqlCommandHelper.Add(cmd, "@depth", run.Depth.ToString());
            PostgresqlCommandHelper.Add(cmd, "@status", run.Status.ToString());
            PostgresqlCommandHelper.Add(cmd, "@is_evaluation", run.IsEvaluation);
            PostgresqlCommandHelper.Add(cmd, "@probe_set_version", run.ProbeSetVersion ?? String.Empty);
            PostgresqlCommandHelper.Add(cmd, "@runtime", run.Runtime);
            PostgresqlCommandHelper.Add(cmd, "@runtime_version", run.RuntimeVersion);
            PostgresqlCommandHelper.Add(cmd, "@model_provider", run.ModelProvider);
            PostgresqlCommandHelper.Add(cmd, "@model", run.Model);
            PostgresqlCommandHelper.Add(cmd, "@model_digest", run.ModelDigest);
            PostgresqlCommandHelper.Add(cmd, "@configured_context_tokens", run.ConfiguredContextTokens);
            PostgresqlCommandHelper.Add(cmd, "@served_context_tokens", run.ServedContextTokens);
            PostgresqlCommandHelper.Add(cmd, "@harbor_id", run.HarborId);
            PostgresqlCommandHelper.Add(cmd, "@inference_host", run.InferenceHost);
            PostgresqlCommandHelper.Add(cmd, "@token_budget", run.TokenBudget);
            PostgresqlCommandHelper.Add(cmd, "@tokens_used", run.TokensUsed);
            PostgresqlCommandHelper.Add(cmd, "@passed_count", run.PassedCount);
            PostgresqlCommandHelper.Add(cmd, "@failed_count", run.FailedCount);
            PostgresqlCommandHelper.Add(cmd, "@could_not_run_count", run.CouldNotRunCount);
            PostgresqlCommandHelper.Add(cmd, "@failure_reason", run.FailureReason);
            PostgresqlCommandHelper.Add(cmd, "@battery_run_id", run.BatteryRunId);
            PostgresqlCommandHelper.Add(cmd, "@report_json", ModelEvaluationReport.ToJson(run.Report));
            PostgresqlCommandHelper.AddDate(cmd, "@started_utc", run.StartedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@completed_utc", run.CompletedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", run.LastUpdateUtc);
        }

        private static ModelProbeRun FromReader(NpgsqlDataReader reader)
        {
            ModelProbeRun run = new ModelProbeRun();
            run.Id = reader["id"].ToString()!;
            run.TenantId = PostgresqlCommandHelper.ReadString(reader["tenant_id"]);
            run.UserId = PostgresqlCommandHelper.ReadString(reader["user_id"]);
            run.TargetKind = PostgresqlCommandHelper.ReadEnum(reader["target_kind"], ModelProbeTargetEnum.Captain);
            run.CaptainId = PostgresqlCommandHelper.ReadString(reader["captain_id"]);
            run.CaptainName = PostgresqlCommandHelper.ReadString(reader["captain_name"]);
            run.ModelEndpointId = PostgresqlCommandHelper.ReadString(reader["model_endpoint_id"]);
            run.Depth = PostgresqlCommandHelper.ReadEnum(reader["depth"], ModelProbeDepthEnum.Quick);
            run.Status = PostgresqlCommandHelper.ReadEnum(reader["status"], ModelProbeRunStatusEnum.Queued);
            run.IsEvaluation = PostgresqlCommandHelper.ReadBool(reader["is_evaluation"], true);
            run.ProbeSetVersion = PostgresqlCommandHelper.ReadString(reader["probe_set_version"]) ?? String.Empty;
            run.Runtime = PostgresqlCommandHelper.ReadString(reader["runtime"]);
            run.RuntimeVersion = PostgresqlCommandHelper.ReadString(reader["runtime_version"]);
            run.ModelProvider = PostgresqlCommandHelper.ReadString(reader["model_provider"]);
            run.Model = PostgresqlCommandHelper.ReadString(reader["model"]);
            run.ModelDigest = PostgresqlCommandHelper.ReadString(reader["model_digest"]);
            run.ConfiguredContextTokens = PostgresqlCommandHelper.ReadNullableInt(reader["configured_context_tokens"]);
            run.ServedContextTokens = PostgresqlCommandHelper.ReadNullableInt(reader["served_context_tokens"]);
            run.HarborId = PostgresqlCommandHelper.ReadString(reader["harbor_id"]);
            run.InferenceHost = PostgresqlCommandHelper.ReadString(reader["inference_host"]);
            run.TokenBudget = PostgresqlCommandHelper.ReadNullableLong(reader["token_budget"]) ?? 0;
            run.TokensUsed = PostgresqlCommandHelper.ReadNullableLong(reader["tokens_used"]) ?? 0;
            run.PassedCount = PostgresqlCommandHelper.ReadInt(reader["passed_count"], 0);
            run.FailedCount = PostgresqlCommandHelper.ReadInt(reader["failed_count"], 0);
            run.CouldNotRunCount = PostgresqlCommandHelper.ReadInt(reader["could_not_run_count"], 0);
            run.FailureReason = PostgresqlCommandHelper.ReadString(reader["failure_reason"]);
            run.BatteryRunId = PostgresqlCommandHelper.ReadString(reader["battery_run_id"]);
            run.Report = ModelEvaluationReport.FromJson(PostgresqlCommandHelper.ReadString(reader["report_json"]));
            run.CreatedUtc = PostgresqlCommandHelper.ReadDate(reader["created_utc"]);
            run.StartedUtc = PostgresqlCommandHelper.ReadNullableDate(reader["started_utc"]);
            run.CompletedUtc = PostgresqlCommandHelper.ReadNullableDate(reader["completed_utc"]);
            run.LastUpdateUtc = PostgresqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return run;
        }

        #endregion
    }
}
