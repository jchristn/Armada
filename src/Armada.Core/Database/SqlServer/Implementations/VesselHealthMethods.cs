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
    /// SQL Server implementation of per-vessel health row persistence. Status columns are stored exactly as given
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
        /// <param name="driver">SQL Server database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public VesselHealthMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<VesselHealth> UpsertAsync(VesselHealth health, CancellationToken token = default)
        {
            if (health == null) throw new ArgumentNullException(nameof(health));
            if (String.IsNullOrEmpty(health.VesselId)) throw new ArgumentException("VesselId is required.", nameof(health));
            if (String.IsNullOrEmpty(health.TenantId)) throw new ArgumentException("TenantId is required.", nameof(health));
            health.LastUpdateUtc = DateTime.UtcNow;

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                List<VesselHealth> existing = await SqlServerCommandHelper.QueryAsync(conn, tx,
                    "SELECT * FROM vessel_health WHERE vessel_id = @vessel_id;",
                    cmd => SqlServerCommandHelper.Add(cmd, "@vessel_id", health.VesselId),
                    FromRow, token).ConfigureAwait(false);

                if (existing.Count > 0)
                {
                    health.Id = existing[0].Id;
                    health.CreatedUtc = existing[0].CreatedUtc;
                    await SqlServerCommandHelper.ExecuteAsync(conn, tx, @"UPDATE vessel_health SET
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
                    await SqlServerCommandHelper.ExecuteAsync(conn, tx, "INSERT INTO vessel_health (" + _Columns + ") VALUES (" + _Values + ");", cmd => Bind(cmd, health), token).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);

            return health;
        }

        /// <inheritdoc />
        public async Task<VesselHealth?> ReadByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            List<VesselHealth> results = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT " + VesselHealthQueryBuilder.SelectColumns +
                " FROM vessels v INNER JOIN vessel_health h ON h.vessel_id = v.id LEFT JOIN fleets f ON f.id = v.fleet_id" +
                " WHERE h.tenant_id = @tenant_id AND h.vessel_id = @vessel_id;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@vessel_id", vesselId);
                },
                FromJoinedRow, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task DeleteByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_health WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<VesselHealth>> EnumerateAsync(string tenantId, VesselHealthEnumerateRequest request, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (request == null) request = new VesselHealthEnumerateRequest();

            VesselHealthQuery query = VesselHealthQueryBuilder.Build(tenantId, request);
            Action<SqlCommand> bind = cmd => BindParameters(cmd, query.Parameters);

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqlServerCommandHelper.CountAsync(conn, "SELECT COUNT(*)" + query.FromClause + query.WhereClause + ";", bind, token).ConfigureAwait(false);
                List<VesselHealth> rows = await SqlServerCommandHelper.QueryAsync(conn,
                    "SELECT " + VesselHealthQueryBuilder.SelectColumns + query.FromClause + query.WhereClause + query.OrderByClause + SqlServerCommandHelper.Page(request.Offset, request.PageSize) + ";",
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
            List<VesselHealthSummary> results = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
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
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@v_active", true);
                },
                SummaryFromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : new VesselHealthSummary();
        }

        #endregion

        #region Private-Methods

        private static void BindParameters(SqlCommand cmd, List<QueryParameter> parameters)
        {
            foreach (QueryParameter parameter in parameters)
            {
                if (parameter.Value is DateTime dt) SqlServerCommandHelper.AddDate(cmd, parameter.Name, dt);
                else SqlServerCommandHelper.Add(cmd, parameter.Name, parameter.Value);
            }
        }

        private static void Bind(SqlCommand cmd, VesselHealth health)
        {
            SqlServerCommandHelper.Add(cmd, "@id", health.Id);
            SqlServerCommandHelper.Add(cmd, "@tenant_id", health.TenantId);
            SqlServerCommandHelper.Add(cmd, "@vessel_id", health.VesselId);
            SqlServerCommandHelper.Add(cmd, "@overall_status", health.OverallStatus.ToString());
            SqlServerCommandHelper.AddDate(cmd, "@evaluated_utc", health.EvaluatedUtc);
            SqlServerCommandHelper.Add(cmd, "@evaluation_duration_ms", health.EvaluationDurationMs);
            SqlServerCommandHelper.Add(cmd, "@error_code", health.ErrorCode);
            SqlServerCommandHelper.Add(cmd, "@evaluated_path", health.EvaluatedPath);
            SqlServerCommandHelper.Add(cmd, "@current_branch", health.CurrentBranch);
            SqlServerCommandHelper.Add(cmd, "@is_dirty", health.IsDirty);
            SqlServerCommandHelper.Add(cmd, "@untracked_count", health.UntrackedCount);
            SqlServerCommandHelper.Add(cmd, "@ahead_of_default", health.AheadOfDefault);
            SqlServerCommandHelper.Add(cmd, "@behind_default", health.BehindDefault);
            SqlServerCommandHelper.Add(cmd, "@ahead_of_upstream", health.AheadOfUpstream);
            SqlServerCommandHelper.Add(cmd, "@behind_upstream", health.BehindUpstream);
            SqlServerCommandHelper.AddDate(cmd, "@last_commit_utc", health.LastCommitUtc);
            SqlServerCommandHelper.Add(cmd, "@branch_count", health.BranchCount);
            SqlServerCommandHelper.Add(cmd, "@stale_branch_count", health.StaleBranchCount);
            SqlServerCommandHelper.Add(cmd, "@armada_branch_count", health.ArmadaBranchCount);
            SqlServerCommandHelper.Add(cmd, "@primary_language", health.PrimaryLanguage);
            SqlServerCommandHelper.Add(cmd, "@project_count", health.ProjectCount);
            SqlServerCommandHelper.Add(cmd, "@outdated_count", health.OutdatedCount);
            SqlServerCommandHelper.Add(cmd, "@outdated_major_count", health.OutdatedMajorCount);
            SqlServerCommandHelper.Add(cmd, "@vulnerable_count", health.VulnerableCount);
            SqlServerCommandHelper.Add(cmd, "@max_vulnerability_severity", health.MaxVulnerabilitySeverity.ToString());
            SqlServerCommandHelper.Add(cmd, "@dependency_status", health.DependencyStatus.ToString());
            SqlServerCommandHelper.Add(cmd, "@vulnerability_status", health.VulnerabilityStatus.ToString());
            SqlServerCommandHelper.Add(cmd, "@test_infra_status", health.TestInfraStatus.ToString());
            SqlServerCommandHelper.Add(cmd, "@ci_status", health.CiStatus.ToString());
            SqlServerCommandHelper.Add(cmd, "@divergence_status", health.DivergenceStatus.ToString());
            SqlServerCommandHelper.Add(cmd, "@working_tree_status", health.WorkingTreeStatus.ToString());
            SqlServerCommandHelper.Add(cmd, "@branch_status", health.BranchStatus.ToString());
            SqlServerCommandHelper.Add(cmd, "@readiness_status", health.ReadinessStatus.ToString());
            SqlServerCommandHelper.Add(cmd, "@mission_outcome_status", health.MissionOutcomeStatus.ToString());
            SqlServerCommandHelper.Add(cmd, "@last_check_run_status", health.LastCheckRunStatus);
            SqlServerCommandHelper.Add(cmd, "@has_ci_config", health.HasCiConfig);
            SqlServerCommandHelper.Add(cmd, "@has_license", health.HasLicense);
            SqlServerCommandHelper.Add(cmd, "@has_readme", health.HasReadme);
            SqlServerCommandHelper.Add(cmd, "@readiness_error_count", health.ReadinessErrorCount);
            SqlServerCommandHelper.Add(cmd, "@recent_mission_failure_count", health.RecentMissionFailureCount);
            SqlServerCommandHelper.Add(cmd, "@manifest_hash", health.ManifestHash);
            SqlServerCommandHelper.AddDate(cmd, "@dependencies_evaluated_utc", health.DependenciesEvaluatedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@created_utc", health.CreatedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", health.LastUpdateUtc);
        }

        private static VesselHealth FromRow(SqlDataReader reader)
        {
            VesselHealth health = new VesselHealth();
            health.Id = SqlServerCommandHelper.ReadString(reader["id"]);
            health.TenantId = SqlServerCommandHelper.ReadString(reader["tenant_id"]);
            health.VesselId = SqlServerCommandHelper.ReadString(reader["vessel_id"]) ?? String.Empty;
            health.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            health.LastUpdateUtc = SqlServerCommandHelper.ReadDate(reader["last_update_utc"]);
            ReadMeasurements(reader, health);
            return health;
        }

        private static VesselHealth FromJoinedRow(SqlDataReader reader)
        {
            VesselHealth health = new VesselHealth();
            health.Id = SqlServerCommandHelper.ReadString(reader["id"]);
            health.TenantId = SqlServerCommandHelper.ReadString(reader["v_tenant_id"]);
            health.VesselId = SqlServerCommandHelper.ReadString(reader["v_id"]) ?? String.Empty;
            health.VesselName = SqlServerCommandHelper.ReadString(reader["v_name"]);
            health.FleetId = SqlServerCommandHelper.ReadString(reader["v_fleet_id"]);
            health.FleetName = SqlServerCommandHelper.ReadString(reader["f_name"]);
            DateTime? created = SqlServerCommandHelper.ReadNullableDate(reader["created_utc"]);
            DateTime? updated = SqlServerCommandHelper.ReadNullableDate(reader["last_update_utc"]);
            if (created.HasValue) health.CreatedUtc = created.Value;
            if (updated.HasValue) health.LastUpdateUtc = updated.Value;
            ReadMeasurements(reader, health);
            return health;
        }

        private static void ReadMeasurements(SqlDataReader reader, VesselHealth health)
        {
            health.OverallStatus = SqlServerCommandHelper.ReadEnum(reader["overall_status"], VesselHealthStatusEnum.Unknown);
            health.EvaluatedUtc = SqlServerCommandHelper.ReadNullableDate(reader["evaluated_utc"]);
            health.EvaluationDurationMs = SqlServerCommandHelper.ReadNullableLong(reader["evaluation_duration_ms"]);
            health.ErrorCode = SqlServerCommandHelper.ReadString(reader["error_code"]);
            health.EvaluatedPath = SqlServerCommandHelper.ReadString(reader["evaluated_path"]);
            health.CurrentBranch = SqlServerCommandHelper.ReadString(reader["current_branch"]);
            health.IsDirty = SqlServerCommandHelper.ReadNullableBool(reader["is_dirty"]);
            health.UntrackedCount = SqlServerCommandHelper.ReadNullableInt(reader["untracked_count"]);
            health.AheadOfDefault = SqlServerCommandHelper.ReadNullableInt(reader["ahead_of_default"]);
            health.BehindDefault = SqlServerCommandHelper.ReadNullableInt(reader["behind_default"]);
            health.AheadOfUpstream = SqlServerCommandHelper.ReadNullableInt(reader["ahead_of_upstream"]);
            health.BehindUpstream = SqlServerCommandHelper.ReadNullableInt(reader["behind_upstream"]);
            health.LastCommitUtc = SqlServerCommandHelper.ReadNullableDate(reader["last_commit_utc"]);
            health.BranchCount = SqlServerCommandHelper.ReadNullableInt(reader["branch_count"]);
            health.StaleBranchCount = SqlServerCommandHelper.ReadNullableInt(reader["stale_branch_count"]);
            health.ArmadaBranchCount = SqlServerCommandHelper.ReadNullableInt(reader["armada_branch_count"]);
            health.PrimaryLanguage = SqlServerCommandHelper.ReadString(reader["primary_language"]);
            health.ProjectCount = SqlServerCommandHelper.ReadNullableInt(reader["project_count"]);
            health.OutdatedCount = SqlServerCommandHelper.ReadNullableInt(reader["outdated_count"]);
            health.OutdatedMajorCount = SqlServerCommandHelper.ReadNullableInt(reader["outdated_major_count"]);
            health.VulnerableCount = SqlServerCommandHelper.ReadNullableInt(reader["vulnerable_count"]);
            health.MaxVulnerabilitySeverity = SqlServerCommandHelper.ReadEnum(reader["max_vulnerability_severity"], VulnerabilitySeverityEnum.None);
            health.DependencyStatus = SqlServerCommandHelper.ReadEnum(reader["dependency_status"], VesselHealthStatusEnum.Unknown);
            health.VulnerabilityStatus = SqlServerCommandHelper.ReadEnum(reader["vulnerability_status"], VesselHealthStatusEnum.Unknown);
            health.TestInfraStatus = SqlServerCommandHelper.ReadEnum(reader["test_infra_status"], VesselHealthStatusEnum.Unknown);
            health.CiStatus = SqlServerCommandHelper.ReadEnum(reader["ci_status"], VesselHealthStatusEnum.Unknown);
            health.DivergenceStatus = SqlServerCommandHelper.ReadEnum(reader["divergence_status"], VesselHealthStatusEnum.Unknown);
            health.WorkingTreeStatus = SqlServerCommandHelper.ReadEnum(reader["working_tree_status"], VesselHealthStatusEnum.Unknown);
            health.BranchStatus = SqlServerCommandHelper.ReadEnum(reader["branch_status"], VesselHealthStatusEnum.Unknown);
            health.ReadinessStatus = SqlServerCommandHelper.ReadEnum(reader["readiness_status"], VesselHealthStatusEnum.Unknown);
            health.MissionOutcomeStatus = SqlServerCommandHelper.ReadEnum(reader["mission_outcome_status"], VesselHealthStatusEnum.Unknown);
            health.LastCheckRunStatus = SqlServerCommandHelper.ReadString(reader["last_check_run_status"]);
            health.HasCiConfig = SqlServerCommandHelper.ReadNullableBool(reader["has_ci_config"]);
            health.HasLicense = SqlServerCommandHelper.ReadNullableBool(reader["has_license"]);
            health.HasReadme = SqlServerCommandHelper.ReadNullableBool(reader["has_readme"]);
            health.ReadinessErrorCount = SqlServerCommandHelper.ReadNullableInt(reader["readiness_error_count"]);
            health.RecentMissionFailureCount = SqlServerCommandHelper.ReadNullableInt(reader["recent_mission_failure_count"]);
            health.ManifestHash = SqlServerCommandHelper.ReadString(reader["manifest_hash"]);
            health.DependenciesEvaluatedUtc = SqlServerCommandHelper.ReadNullableDate(reader["dependencies_evaluated_utc"]);
        }

        private static VesselHealthSummary SummaryFromReader(SqlDataReader reader)
        {
            VesselHealthSummary summary = new VesselHealthSummary();
            summary.TotalVessels = SqlServerCommandHelper.ReadNullableLong(reader["total_vessels"]) ?? 0;
            summary.NotEvaluated = SqlServerCommandHelper.ReadNullableLong(reader["not_evaluated"]) ?? 0;
            summary.Pass = SqlServerCommandHelper.ReadNullableLong(reader["pass_count"]) ?? 0;
            summary.Warn = SqlServerCommandHelper.ReadNullableLong(reader["warn_count"]) ?? 0;
            summary.Fail = SqlServerCommandHelper.ReadNullableLong(reader["fail_count"]) ?? 0;
            summary.Unknown = SqlServerCommandHelper.ReadNullableLong(reader["unknown_count"]) ?? 0;
            summary.NotApplicable = SqlServerCommandHelper.ReadNullableLong(reader["not_applicable_count"]) ?? 0;
            summary.OutdatedMajorVessels = SqlServerCommandHelper.ReadNullableLong(reader["outdated_major_vessels"]) ?? 0;
            summary.HighOrCriticalVulnerabilityVessels = SqlServerCommandHelper.ReadNullableLong(reader["high_critical_vessels"]) ?? 0;
            return summary;
        }

        #endregion
    }
}
