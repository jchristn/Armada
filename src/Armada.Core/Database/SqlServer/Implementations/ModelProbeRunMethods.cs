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
    /// SQL Server implementation of model probe run persistence.
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
        /// <param name="driver">SQL Server database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public ModelProbeRunMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, run, true), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return run;
        }

        /// <inheritdoc />
        public async Task<ModelProbeRun?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<ModelProbeRun> rows = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM model_probe_runs WHERE id = @id;",
                cmd => SqlServerCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<ModelProbeRun> UpdateAsync(ModelProbeRun run, CancellationToken token = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            run.LastUpdateUtc = DateTime.UtcNow;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, _Update, cmd => Bind(cmd, run, false), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return run;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int deleted = 0;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM model_probe_results WHERE run_id = @id;",
                    cmd => SqlServerCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
                deleted = await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM model_probe_runs WHERE id = @id;",
                    cmd => SqlServerCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
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

            Action<SqlCommand> bind = cmd =>
            {
                if (query.TenantId != null) SqlServerCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.CaptainId != null) SqlServerCommandHelper.Add(cmd, "@captain_id", query.CaptainId);
                if (query.ModelEndpointId != null) SqlServerCommandHelper.Add(cmd, "@model_endpoint_id", query.ModelEndpointId);
                if (query.Status.HasValue) SqlServerCommandHelper.Add(cmd, "@status", query.Status.Value.ToString());
                if (query.FromUtc.HasValue) SqlServerCommandHelper.AddDate(cmd, "@from_utc", query.FromUtc.Value);
            };

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqlServerCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM model_probe_runs" + where + ";", bind, token).ConfigureAwait(false);
                List<ModelProbeRun> rows = await SqlServerCommandHelper.QueryAsync(conn,
                    "SELECT * FROM model_probe_runs" + where + " ORDER BY created_utc DESC, id DESC" + SqlServerCommandHelper.Page(offset, pageSize) + ";",
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
            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                return await SqlServerCommandHelper.CountAsync(conn, sql, cmd =>
                {
                    SqlServerCommandHelper.AddDate(cmd, "@since_utc", sinceUtc);
                    if (inferenceHost != null) SqlServerCommandHelper.Add(cmd, "@inference_host", inferenceHost);
                }, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqlCommand cmd, ModelProbeRun run, bool includeCreateColumns)
        {
            SqlServerCommandHelper.Add(cmd, "@id", run.Id);
            if (includeCreateColumns)
            {
                SqlServerCommandHelper.Add(cmd, "@tenant_id", run.TenantId);
                SqlServerCommandHelper.Add(cmd, "@user_id", run.UserId);
                SqlServerCommandHelper.AddDate(cmd, "@created_utc", run.CreatedUtc);
            }

            SqlServerCommandHelper.Add(cmd, "@target_kind", run.TargetKind.ToString());
            SqlServerCommandHelper.Add(cmd, "@captain_id", run.CaptainId);
            SqlServerCommandHelper.Add(cmd, "@captain_name", run.CaptainName);
            SqlServerCommandHelper.Add(cmd, "@model_endpoint_id", run.ModelEndpointId);
            SqlServerCommandHelper.Add(cmd, "@depth", run.Depth.ToString());
            SqlServerCommandHelper.Add(cmd, "@status", run.Status.ToString());
            SqlServerCommandHelper.Add(cmd, "@is_evaluation", run.IsEvaluation);
            SqlServerCommandHelper.Add(cmd, "@probe_set_version", run.ProbeSetVersion ?? String.Empty);
            SqlServerCommandHelper.Add(cmd, "@runtime", run.Runtime);
            SqlServerCommandHelper.Add(cmd, "@runtime_version", run.RuntimeVersion);
            SqlServerCommandHelper.Add(cmd, "@model_provider", run.ModelProvider);
            SqlServerCommandHelper.Add(cmd, "@model", run.Model);
            SqlServerCommandHelper.Add(cmd, "@model_digest", run.ModelDigest);
            SqlServerCommandHelper.Add(cmd, "@configured_context_tokens", run.ConfiguredContextTokens);
            SqlServerCommandHelper.Add(cmd, "@served_context_tokens", run.ServedContextTokens);
            SqlServerCommandHelper.Add(cmd, "@harbor_id", run.HarborId);
            SqlServerCommandHelper.Add(cmd, "@inference_host", run.InferenceHost);
            SqlServerCommandHelper.Add(cmd, "@token_budget", run.TokenBudget);
            SqlServerCommandHelper.Add(cmd, "@tokens_used", run.TokensUsed);
            SqlServerCommandHelper.Add(cmd, "@passed_count", run.PassedCount);
            SqlServerCommandHelper.Add(cmd, "@failed_count", run.FailedCount);
            SqlServerCommandHelper.Add(cmd, "@could_not_run_count", run.CouldNotRunCount);
            SqlServerCommandHelper.Add(cmd, "@failure_reason", run.FailureReason);
            SqlServerCommandHelper.Add(cmd, "@battery_run_id", run.BatteryRunId);
            SqlServerCommandHelper.Add(cmd, "@report_json", ModelEvaluationReport.ToJson(run.Report));
            SqlServerCommandHelper.AddDate(cmd, "@started_utc", run.StartedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@completed_utc", run.CompletedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", run.LastUpdateUtc);
        }

        private static ModelProbeRun FromReader(SqlDataReader reader)
        {
            ModelProbeRun run = new ModelProbeRun();
            run.Id = reader["id"].ToString()!;
            run.TenantId = SqlServerCommandHelper.ReadString(reader["tenant_id"]);
            run.UserId = SqlServerCommandHelper.ReadString(reader["user_id"]);
            run.TargetKind = SqlServerCommandHelper.ReadEnum(reader["target_kind"], ModelProbeTargetEnum.Captain);
            run.CaptainId = SqlServerCommandHelper.ReadString(reader["captain_id"]);
            run.CaptainName = SqlServerCommandHelper.ReadString(reader["captain_name"]);
            run.ModelEndpointId = SqlServerCommandHelper.ReadString(reader["model_endpoint_id"]);
            run.Depth = SqlServerCommandHelper.ReadEnum(reader["depth"], ModelProbeDepthEnum.Quick);
            run.Status = SqlServerCommandHelper.ReadEnum(reader["status"], ModelProbeRunStatusEnum.Queued);
            run.IsEvaluation = SqlServerCommandHelper.ReadBool(reader["is_evaluation"], true);
            run.ProbeSetVersion = SqlServerCommandHelper.ReadString(reader["probe_set_version"]) ?? String.Empty;
            run.Runtime = SqlServerCommandHelper.ReadString(reader["runtime"]);
            run.RuntimeVersion = SqlServerCommandHelper.ReadString(reader["runtime_version"]);
            run.ModelProvider = SqlServerCommandHelper.ReadString(reader["model_provider"]);
            run.Model = SqlServerCommandHelper.ReadString(reader["model"]);
            run.ModelDigest = SqlServerCommandHelper.ReadString(reader["model_digest"]);
            run.ConfiguredContextTokens = SqlServerCommandHelper.ReadNullableInt(reader["configured_context_tokens"]);
            run.ServedContextTokens = SqlServerCommandHelper.ReadNullableInt(reader["served_context_tokens"]);
            run.HarborId = SqlServerCommandHelper.ReadString(reader["harbor_id"]);
            run.InferenceHost = SqlServerCommandHelper.ReadString(reader["inference_host"]);
            run.TokenBudget = SqlServerCommandHelper.ReadNullableLong(reader["token_budget"]) ?? 0;
            run.TokensUsed = SqlServerCommandHelper.ReadNullableLong(reader["tokens_used"]) ?? 0;
            run.PassedCount = SqlServerCommandHelper.ReadInt(reader["passed_count"], 0);
            run.FailedCount = SqlServerCommandHelper.ReadInt(reader["failed_count"], 0);
            run.CouldNotRunCount = SqlServerCommandHelper.ReadInt(reader["could_not_run_count"], 0);
            run.FailureReason = SqlServerCommandHelper.ReadString(reader["failure_reason"]);
            run.BatteryRunId = SqlServerCommandHelper.ReadString(reader["battery_run_id"]);
            run.Report = ModelEvaluationReport.FromJson(SqlServerCommandHelper.ReadString(reader["report_json"]));
            run.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            run.StartedUtc = SqlServerCommandHelper.ReadNullableDate(reader["started_utc"]);
            run.CompletedUtc = SqlServerCommandHelper.ReadNullableDate(reader["completed_utc"]);
            run.LastUpdateUtc = SqlServerCommandHelper.ReadDate(reader["last_update_utc"]);
            return run;
        }

        #endregion
    }
}
