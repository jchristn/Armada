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
    /// MySQL implementation of per-vessel health row persistence. Status columns are stored exactly as given
    /// (the evaluator writes effective, override-aware values). Enumeration selects from vessels left-joined to
    /// vessel_health and fleets so vessels that were never evaluated still appear.
    /// </summary>
    public class VesselHealthMethods : IVesselHealthMethods
    {
        #region Private-Members

        private static readonly string _Columns = @"id, tenant_id, vessel_id, overall_status, evaluated_utc, evaluation_duration_ms, error_code,
            evaluated_path, current_branch, is_dirty, untracked_count, ahead_of_default, behind_default,
            ahead_of_upstream, behind_upstream, last_commit_utc, branch_count, stale_branch_count,
            armada_branch_count, primary_language, project_count, outdated_count, outdated_major_count,
            vulnerable_count, max_vulnerability_severity, dependency_status, vulnerability_status,
            test_infra_status, ci_status, divergence_status, working_tree_status, branch_status,
            readiness_status, mission_outcome_status, last_check_run_status, has_ci_config, has_license,
            has_readme, readiness_error_count, recent_mission_failure_count, manifest_hash,
            dependencies_evaluated_utc, created_utc, last_update_utc";

        private static readonly string _Values = @"@id, @tenant_id, @vessel_id, @overall_status, @evaluated_utc, @evaluation_duration_ms, @error_code,
            @evaluated_path, @current_branch, @is_dirty, @untracked_count, @ahead_of_default, @behind_default,
            @ahead_of_upstream, @behind_upstream, @last_commit_utc, @branch_count, @stale_branch_count,
            @armada_branch_count, @primary_language, @project_count, @outdated_count, @outdated_major_count,
            @vulnerable_count, @max_vulnerability_severity, @dependency_status, @vulnerability_status,
            @test_infra_status, @ci_status, @divergence_status, @working_tree_status, @branch_status,
            @readiness_status, @mission_outcome_status, @last_check_run_status, @has_ci_config, @has_license,
            @has_readme, @readiness_error_count, @recent_mission_failure_count, @manifest_hash,
            @dependencies_evaluated_utc, @created_utc, @last_update_utc";

        private readonly string _ConnectionString;
        private readonly SemaphoreSlim? _WriteLock;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        public VesselHealthMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
        }
        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<VesselHealth> UpsertAsync(VesselHealth health, CancellationToken token = default)
        {
            if (health == null) throw new ArgumentNullException(nameof(health));
            if (String.IsNullOrEmpty(health.VesselId)) throw new ArgumentException("VesselId is required.", nameof(health));
            if (String.IsNullOrEmpty(health.TenantId)) throw new ArgumentException("TenantId is required.", nameof(health));
            health.LastUpdateUtc = DateTime.UtcNow;

            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                List<VesselHealth> existing = await MysqlCommandHelper.QueryAsync(conn, tx,
                    "SELECT * FROM vessel_health WHERE vessel_id = @vessel_id;",
                    cmd => MysqlCommandHelper.Add(cmd, "@vessel_id", health.VesselId),
                    FromRow, token).ConfigureAwait(false);

                if (existing.Count > 0)
                {
                    health.Id = existing[0].Id;
                    health.CreatedUtc = existing[0].CreatedUtc;
                    await MysqlCommandHelper.ExecuteAsync(conn, tx, @"UPDATE vessel_health SET
                    tenant_id = @tenant_id, vessel_id = @vessel_id, overall_status = @overall_status,
                    evaluated_utc = @evaluated_utc, evaluation_duration_ms = @evaluation_duration_ms,
                    error_code = @error_code, evaluated_path = @evaluated_path, current_branch = @current_branch,
                    is_dirty = @is_dirty, untracked_count = @untracked_count, ahead_of_default = @ahead_of_default,
                    behind_default = @behind_default, ahead_of_upstream = @ahead_of_upstream,
                    behind_upstream = @behind_upstream, last_commit_utc = @last_commit_utc, branch_count = @branch_count,
                    stale_branch_count = @stale_branch_count, armada_branch_count = @armada_branch_count,
                    primary_language = @primary_language, project_count = @project_count,
                    outdated_count = @outdated_count, outdated_major_count = @outdated_major_count,
                    vulnerable_count = @vulnerable_count, max_vulnerability_severity = @max_vulnerability_severity,
                    dependency_status = @dependency_status, vulnerability_status = @vulnerability_status,
                    test_infra_status = @test_infra_status, ci_status = @ci_status,
                    divergence_status = @divergence_status, working_tree_status = @working_tree_status,
                    branch_status = @branch_status, readiness_status = @readiness_status,
                    mission_outcome_status = @mission_outcome_status, last_check_run_status = @last_check_run_status,
                    has_ci_config = @has_ci_config, has_license = @has_license, has_readme = @has_readme,
                    readiness_error_count = @readiness_error_count,
                    recent_mission_failure_count = @recent_mission_failure_count, manifest_hash = @manifest_hash,
                    dependencies_evaluated_utc = @dependencies_evaluated_utc, last_update_utc = @last_update_utc
                    WHERE vessel_id = @vessel_id;", cmd => Bind(cmd, health), token).ConfigureAwait(false);
                }
                else
                {
                    if (String.IsNullOrEmpty(health.Id)) health.Id = Constants.IdGenerator.GenerateKSortable(Constants.VesselHealthIdPrefix, 24);
                    await MysqlCommandHelper.ExecuteAsync(conn, tx, "INSERT INTO vessel_health (" + _Columns + ") VALUES (" + _Values + ");", cmd => Bind(cmd, health), token).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);

            return health;
        }

        /// <inheritdoc />
        public async Task<VesselHealth?> ReadByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            List<VesselHealth> results = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT " + VesselHealthQueryBuilder.SelectColumns +
                " FROM vessels v INNER JOIN vessel_health h ON h.vessel_id = v.id LEFT JOIN fleets f ON f.id = v.fleet_id" +
                " WHERE h.tenant_id = @tenant_id AND h.vessel_id = @vessel_id;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                },
                FromJoinedRow, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task DeleteByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_health WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<VesselHealth>> EnumerateAsync(string tenantId, VesselHealthEnumerateRequest request, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (request == null) request = new VesselHealthEnumerateRequest();

            VesselHealthQuery query = VesselHealthQueryBuilder.Build(tenantId, request);
            Action<MySqlCommand> bind = cmd => BindParameters(cmd, query.Parameters);

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*)" + query.FromClause + query.WhereClause + ";", bind, token).ConfigureAwait(false);
                List<VesselHealth> rows = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT " + VesselHealthQueryBuilder.SelectColumns + query.FromClause + query.WhereClause + query.OrderByClause + MysqlCommandHelper.Page(request.Offset, request.PageSize) + ";",
                    bind, FromJoinedRow, token).ConfigureAwait(false);

                EnumerationResult<VesselHealth> result = new EnumerationResult<VesselHealth>();
                result.PageNumber = request.PageNumber;
                result.PageSize = request.PageSize;
                result.TotalRecords = total;
                result.TotalPages = (int)Math.Ceiling((double)total / request.PageSize);
                result.Objects = rows;
                return result;
            }
        }

        /// <inheritdoc />
        public async Task<VesselHealthSummary> CountByOverallStatusAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            List<VesselHealthSummary> results = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                @"SELECT COUNT(*) AS total_vessels,
                    SUM(CASE WHEN h.id IS NULL THEN 1 ELSE 0 END) AS not_evaluated,
                    SUM(CASE WHEN h.overall_status = 'Pass' THEN 1 ELSE 0 END) AS pass_count,
                    SUM(CASE WHEN h.overall_status = 'Warn' THEN 1 ELSE 0 END) AS warn_count,
                    SUM(CASE WHEN h.overall_status = 'Fail' THEN 1 ELSE 0 END) AS fail_count,
                    SUM(CASE WHEN h.overall_status = 'Unknown' THEN 1 ELSE 0 END) AS unknown_count,
                    SUM(CASE WHEN h.overall_status = 'NotApplicable' THEN 1 ELSE 0 END) AS not_applicable_count,
                    SUM(CASE WHEN h.outdated_major_count > 0 THEN 1 ELSE 0 END) AS outdated_major_vessels,
                    SUM(CASE WHEN h.max_vulnerability_severity IN ('High', 'Critical') THEN 1 ELSE 0 END) AS high_critical_vessels
                  FROM vessels v LEFT JOIN vessel_health h ON h.vessel_id = v.id
                  WHERE v.tenant_id = @tenant_id AND v.active = @v_active;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@v_active", true);
                },
                SummaryFromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : new VesselHealthSummary();
        }

        #endregion

        #region Private-Methods

        private static void BindParameters(MySqlCommand cmd, List<QueryParameter> parameters)
        {
            foreach (QueryParameter parameter in parameters)
            {
                if (parameter.Value is DateTime dt) MysqlCommandHelper.AddDate(cmd, parameter.Name, dt);
                else MysqlCommandHelper.Add(cmd, parameter.Name, parameter.Value);
            }
        }

        private static void Bind(MySqlCommand cmd, VesselHealth health)
        {
            MysqlCommandHelper.Add(cmd, "@id", health.Id);
            MysqlCommandHelper.Add(cmd, "@tenant_id", health.TenantId);
            MysqlCommandHelper.Add(cmd, "@vessel_id", health.VesselId);
            MysqlCommandHelper.Add(cmd, "@overall_status", health.OverallStatus.ToString());
            MysqlCommandHelper.AddDate(cmd, "@evaluated_utc", health.EvaluatedUtc);
            MysqlCommandHelper.Add(cmd, "@evaluation_duration_ms", health.EvaluationDurationMs);
            MysqlCommandHelper.Add(cmd, "@error_code", health.ErrorCode);
            MysqlCommandHelper.Add(cmd, "@evaluated_path", health.EvaluatedPath);
            MysqlCommandHelper.Add(cmd, "@current_branch", health.CurrentBranch);
            MysqlCommandHelper.Add(cmd, "@is_dirty", health.IsDirty);
            MysqlCommandHelper.Add(cmd, "@untracked_count", health.UntrackedCount);
            MysqlCommandHelper.Add(cmd, "@ahead_of_default", health.AheadOfDefault);
            MysqlCommandHelper.Add(cmd, "@behind_default", health.BehindDefault);
            MysqlCommandHelper.Add(cmd, "@ahead_of_upstream", health.AheadOfUpstream);
            MysqlCommandHelper.Add(cmd, "@behind_upstream", health.BehindUpstream);
            MysqlCommandHelper.AddDate(cmd, "@last_commit_utc", health.LastCommitUtc);
            MysqlCommandHelper.Add(cmd, "@branch_count", health.BranchCount);
            MysqlCommandHelper.Add(cmd, "@stale_branch_count", health.StaleBranchCount);
            MysqlCommandHelper.Add(cmd, "@armada_branch_count", health.ArmadaBranchCount);
            MysqlCommandHelper.Add(cmd, "@primary_language", health.PrimaryLanguage);
            MysqlCommandHelper.Add(cmd, "@project_count", health.ProjectCount);
            MysqlCommandHelper.Add(cmd, "@outdated_count", health.OutdatedCount);
            MysqlCommandHelper.Add(cmd, "@outdated_major_count", health.OutdatedMajorCount);
            MysqlCommandHelper.Add(cmd, "@vulnerable_count", health.VulnerableCount);
            MysqlCommandHelper.Add(cmd, "@max_vulnerability_severity", health.MaxVulnerabilitySeverity.ToString());
            MysqlCommandHelper.Add(cmd, "@dependency_status", health.DependencyStatus.ToString());
            MysqlCommandHelper.Add(cmd, "@vulnerability_status", health.VulnerabilityStatus.ToString());
            MysqlCommandHelper.Add(cmd, "@test_infra_status", health.TestInfraStatus.ToString());
            MysqlCommandHelper.Add(cmd, "@ci_status", health.CiStatus.ToString());
            MysqlCommandHelper.Add(cmd, "@divergence_status", health.DivergenceStatus.ToString());
            MysqlCommandHelper.Add(cmd, "@working_tree_status", health.WorkingTreeStatus.ToString());
            MysqlCommandHelper.Add(cmd, "@branch_status", health.BranchStatus.ToString());
            MysqlCommandHelper.Add(cmd, "@readiness_status", health.ReadinessStatus.ToString());
            MysqlCommandHelper.Add(cmd, "@mission_outcome_status", health.MissionOutcomeStatus.ToString());
            MysqlCommandHelper.Add(cmd, "@last_check_run_status", health.LastCheckRunStatus);
            MysqlCommandHelper.Add(cmd, "@has_ci_config", health.HasCiConfig);
            MysqlCommandHelper.Add(cmd, "@has_license", health.HasLicense);
            MysqlCommandHelper.Add(cmd, "@has_readme", health.HasReadme);
            MysqlCommandHelper.Add(cmd, "@readiness_error_count", health.ReadinessErrorCount);
            MysqlCommandHelper.Add(cmd, "@recent_mission_failure_count", health.RecentMissionFailureCount);
            MysqlCommandHelper.Add(cmd, "@manifest_hash", health.ManifestHash);
            MysqlCommandHelper.AddDate(cmd, "@dependencies_evaluated_utc", health.DependenciesEvaluatedUtc);
            MysqlCommandHelper.AddDate(cmd, "@created_utc", health.CreatedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", health.LastUpdateUtc);
        }

        private static VesselHealth FromRow(MySqlDataReader reader)
        {
            VesselHealth health = new VesselHealth();
            health.Id = MysqlCommandHelper.ReadString(reader["id"]);
            health.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            health.VesselId = MysqlCommandHelper.ReadString(reader["vessel_id"]) ?? String.Empty;
            health.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            health.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            ReadMeasurements(reader, health);
            return health;
        }

        private static VesselHealth FromJoinedRow(MySqlDataReader reader)
        {
            VesselHealth health = new VesselHealth();
            health.Id = MysqlCommandHelper.ReadString(reader["id"]);
            health.TenantId = MysqlCommandHelper.ReadString(reader["v_tenant_id"]);
            health.VesselId = MysqlCommandHelper.ReadString(reader["v_id"]) ?? String.Empty;
            health.VesselName = MysqlCommandHelper.ReadString(reader["v_name"]);
            health.FleetId = MysqlCommandHelper.ReadString(reader["v_fleet_id"]);
            health.FleetName = MysqlCommandHelper.ReadString(reader["f_name"]);
            DateTime? created = MysqlCommandHelper.ReadNullableDate(reader["created_utc"]);
            DateTime? updated = MysqlCommandHelper.ReadNullableDate(reader["last_update_utc"]);
            if (created.HasValue) health.CreatedUtc = created.Value;
            if (updated.HasValue) health.LastUpdateUtc = updated.Value;
            ReadMeasurements(reader, health);
            return health;
        }

        private static void ReadMeasurements(MySqlDataReader reader, VesselHealth health)
        {
            health.OverallStatus = MysqlCommandHelper.ReadEnum(reader["overall_status"], VesselHealthStatusEnum.Unknown);
            health.EvaluatedUtc = MysqlCommandHelper.ReadNullableDate(reader["evaluated_utc"]);
            health.EvaluationDurationMs = MysqlCommandHelper.ReadNullableLong(reader["evaluation_duration_ms"]);
            health.ErrorCode = MysqlCommandHelper.ReadString(reader["error_code"]);
            health.EvaluatedPath = MysqlCommandHelper.ReadString(reader["evaluated_path"]);
            health.CurrentBranch = MysqlCommandHelper.ReadString(reader["current_branch"]);
            health.IsDirty = MysqlCommandHelper.ReadNullableBool(reader["is_dirty"]);
            health.UntrackedCount = MysqlCommandHelper.ReadNullableInt(reader["untracked_count"]);
            health.AheadOfDefault = MysqlCommandHelper.ReadNullableInt(reader["ahead_of_default"]);
            health.BehindDefault = MysqlCommandHelper.ReadNullableInt(reader["behind_default"]);
            health.AheadOfUpstream = MysqlCommandHelper.ReadNullableInt(reader["ahead_of_upstream"]);
            health.BehindUpstream = MysqlCommandHelper.ReadNullableInt(reader["behind_upstream"]);
            health.LastCommitUtc = MysqlCommandHelper.ReadNullableDate(reader["last_commit_utc"]);
            health.BranchCount = MysqlCommandHelper.ReadNullableInt(reader["branch_count"]);
            health.StaleBranchCount = MysqlCommandHelper.ReadNullableInt(reader["stale_branch_count"]);
            health.ArmadaBranchCount = MysqlCommandHelper.ReadNullableInt(reader["armada_branch_count"]);
            health.PrimaryLanguage = MysqlCommandHelper.ReadString(reader["primary_language"]);
            health.ProjectCount = MysqlCommandHelper.ReadNullableInt(reader["project_count"]);
            health.OutdatedCount = MysqlCommandHelper.ReadNullableInt(reader["outdated_count"]);
            health.OutdatedMajorCount = MysqlCommandHelper.ReadNullableInt(reader["outdated_major_count"]);
            health.VulnerableCount = MysqlCommandHelper.ReadNullableInt(reader["vulnerable_count"]);
            health.MaxVulnerabilitySeverity = MysqlCommandHelper.ReadEnum(reader["max_vulnerability_severity"], VulnerabilitySeverityEnum.None);
            health.DependencyStatus = MysqlCommandHelper.ReadEnum(reader["dependency_status"], VesselHealthStatusEnum.Unknown);
            health.VulnerabilityStatus = MysqlCommandHelper.ReadEnum(reader["vulnerability_status"], VesselHealthStatusEnum.Unknown);
            health.TestInfraStatus = MysqlCommandHelper.ReadEnum(reader["test_infra_status"], VesselHealthStatusEnum.Unknown);
            health.CiStatus = MysqlCommandHelper.ReadEnum(reader["ci_status"], VesselHealthStatusEnum.Unknown);
            health.DivergenceStatus = MysqlCommandHelper.ReadEnum(reader["divergence_status"], VesselHealthStatusEnum.Unknown);
            health.WorkingTreeStatus = MysqlCommandHelper.ReadEnum(reader["working_tree_status"], VesselHealthStatusEnum.Unknown);
            health.BranchStatus = MysqlCommandHelper.ReadEnum(reader["branch_status"], VesselHealthStatusEnum.Unknown);
            health.ReadinessStatus = MysqlCommandHelper.ReadEnum(reader["readiness_status"], VesselHealthStatusEnum.Unknown);
            health.MissionOutcomeStatus = MysqlCommandHelper.ReadEnum(reader["mission_outcome_status"], VesselHealthStatusEnum.Unknown);
            health.LastCheckRunStatus = MysqlCommandHelper.ReadString(reader["last_check_run_status"]);
            health.HasCiConfig = MysqlCommandHelper.ReadNullableBool(reader["has_ci_config"]);
            health.HasLicense = MysqlCommandHelper.ReadNullableBool(reader["has_license"]);
            health.HasReadme = MysqlCommandHelper.ReadNullableBool(reader["has_readme"]);
            health.ReadinessErrorCount = MysqlCommandHelper.ReadNullableInt(reader["readiness_error_count"]);
            health.RecentMissionFailureCount = MysqlCommandHelper.ReadNullableInt(reader["recent_mission_failure_count"]);
            health.ManifestHash = MysqlCommandHelper.ReadString(reader["manifest_hash"]);
            health.DependenciesEvaluatedUtc = MysqlCommandHelper.ReadNullableDate(reader["dependencies_evaluated_utc"]);
        }

        private static VesselHealthSummary SummaryFromReader(MySqlDataReader reader)
        {
            VesselHealthSummary summary = new VesselHealthSummary();
            summary.TotalVessels = MysqlCommandHelper.ReadNullableLong(reader["total_vessels"]) ?? 0;
            summary.NotEvaluated = MysqlCommandHelper.ReadNullableLong(reader["not_evaluated"]) ?? 0;
            summary.Pass = MysqlCommandHelper.ReadNullableLong(reader["pass_count"]) ?? 0;
            summary.Warn = MysqlCommandHelper.ReadNullableLong(reader["warn_count"]) ?? 0;
            summary.Fail = MysqlCommandHelper.ReadNullableLong(reader["fail_count"]) ?? 0;
            summary.Unknown = MysqlCommandHelper.ReadNullableLong(reader["unknown_count"]) ?? 0;
            summary.NotApplicable = MysqlCommandHelper.ReadNullableLong(reader["not_applicable_count"]) ?? 0;
            summary.OutdatedMajorVessels = MysqlCommandHelper.ReadNullableLong(reader["outdated_major_vessels"]) ?? 0;
            summary.HighOrCriticalVulnerabilityVessels = MysqlCommandHelper.ReadNullableLong(reader["high_critical_vessels"]) ?? 0;
            return summary;
        }

        #endregion
    }
}
