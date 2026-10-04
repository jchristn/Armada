namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// PostgreSQL implementation of per-vessel health row persistence. Status columns are stored exactly as given
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
        /// <param name="driver">PostgreSQL database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public VesselHealthMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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

            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                List<VesselHealth> existing = await PostgresqlCommandHelper.QueryAsync(conn, tx,
                    "SELECT * FROM vessel_health WHERE vessel_id = @vessel_id;",
                    cmd => PostgresqlCommandHelper.Add(cmd, "@vessel_id", health.VesselId),
                    FromRow, token).ConfigureAwait(false);

                if (existing.Count > 0)
                {
                    health.Id = existing[0].Id;
                    health.CreatedUtc = existing[0].CreatedUtc;
                    await PostgresqlCommandHelper.ExecuteAsync(conn, tx, @"UPDATE vessel_health SET
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
                    await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "INSERT INTO vessel_health (" + _Columns + ") VALUES (" + _Values + ");", cmd => Bind(cmd, health), token).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);

            return health;
        }

        /// <inheritdoc />
        public async Task<VesselHealth?> ReadByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            List<VesselHealth> results = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT " + VesselHealthQueryBuilder.SelectColumns +
                " FROM vessels v INNER JOIN vessel_health h ON h.vessel_id = v.id LEFT JOIN fleets f ON f.id = v.fleet_id" +
                " WHERE h.tenant_id = @tenant_id AND h.vessel_id = @vessel_id;",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                },
                FromJoinedRow, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <inheritdoc />
        public async Task DeleteByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_health WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<VesselHealth>> EnumerateAsync(string tenantId, VesselHealthEnumerateRequest request, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (request == null) request = new VesselHealthEnumerateRequest();

            VesselHealthQuery query = VesselHealthQueryBuilder.Build(tenantId, request);
            Action<NpgsqlCommand> bind = cmd => BindParameters(cmd, query.Parameters);

            using (NpgsqlConnection conn = new NpgsqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await PostgresqlCommandHelper.CountAsync(conn, "SELECT COUNT(*)" + query.FromClause + query.WhereClause + ";", bind, token).ConfigureAwait(false);
                List<VesselHealth> rows = await PostgresqlCommandHelper.QueryAsync(conn,
                    "SELECT " + VesselHealthQueryBuilder.SelectColumns + query.FromClause + query.WhereClause + query.OrderByClause + PostgresqlCommandHelper.Page(request.Offset, request.PageSize) + ";",
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
            List<VesselHealthSummary> results = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
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
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@v_active", true);
                },
                SummaryFromReader, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : new VesselHealthSummary();
        }

        #endregion

        #region Private-Methods

        private static void BindParameters(NpgsqlCommand cmd, List<QueryParameter> parameters)
        {
            foreach (QueryParameter parameter in parameters)
            {
                if (parameter.Value is DateTime dt) PostgresqlCommandHelper.AddDate(cmd, parameter.Name, dt);
                else PostgresqlCommandHelper.Add(cmd, parameter.Name, parameter.Value);
            }
        }

        private static void Bind(NpgsqlCommand cmd, VesselHealth health)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", health.Id);
            PostgresqlCommandHelper.Add(cmd, "@tenant_id", health.TenantId);
            PostgresqlCommandHelper.Add(cmd, "@vessel_id", health.VesselId);
            PostgresqlCommandHelper.Add(cmd, "@overall_status", health.OverallStatus.ToString());
            PostgresqlCommandHelper.AddDate(cmd, "@evaluated_utc", health.EvaluatedUtc);
            PostgresqlCommandHelper.Add(cmd, "@evaluation_duration_ms", health.EvaluationDurationMs);
            PostgresqlCommandHelper.Add(cmd, "@error_code", health.ErrorCode);
            PostgresqlCommandHelper.Add(cmd, "@evaluated_path", health.EvaluatedPath);
            PostgresqlCommandHelper.Add(cmd, "@current_branch", health.CurrentBranch);
            PostgresqlCommandHelper.Add(cmd, "@is_dirty", health.IsDirty);
            PostgresqlCommandHelper.Add(cmd, "@untracked_count", health.UntrackedCount);
            PostgresqlCommandHelper.Add(cmd, "@ahead_of_default", health.AheadOfDefault);
            PostgresqlCommandHelper.Add(cmd, "@behind_default", health.BehindDefault);
            PostgresqlCommandHelper.Add(cmd, "@ahead_of_upstream", health.AheadOfUpstream);
            PostgresqlCommandHelper.Add(cmd, "@behind_upstream", health.BehindUpstream);
            PostgresqlCommandHelper.AddDate(cmd, "@last_commit_utc", health.LastCommitUtc);
            PostgresqlCommandHelper.Add(cmd, "@branch_count", health.BranchCount);
            PostgresqlCommandHelper.Add(cmd, "@stale_branch_count", health.StaleBranchCount);
            PostgresqlCommandHelper.Add(cmd, "@armada_branch_count", health.ArmadaBranchCount);
            PostgresqlCommandHelper.Add(cmd, "@primary_language", health.PrimaryLanguage);
            PostgresqlCommandHelper.Add(cmd, "@project_count", health.ProjectCount);
            PostgresqlCommandHelper.Add(cmd, "@outdated_count", health.OutdatedCount);
            PostgresqlCommandHelper.Add(cmd, "@outdated_major_count", health.OutdatedMajorCount);
            PostgresqlCommandHelper.Add(cmd, "@vulnerable_count", health.VulnerableCount);
            PostgresqlCommandHelper.Add(cmd, "@max_vulnerability_severity", health.MaxVulnerabilitySeverity.ToString());
            PostgresqlCommandHelper.Add(cmd, "@dependency_status", health.DependencyStatus.ToString());
            PostgresqlCommandHelper.Add(cmd, "@vulnerability_status", health.VulnerabilityStatus.ToString());
            PostgresqlCommandHelper.Add(cmd, "@test_infra_status", health.TestInfraStatus.ToString());
            PostgresqlCommandHelper.Add(cmd, "@ci_status", health.CiStatus.ToString());
            PostgresqlCommandHelper.Add(cmd, "@divergence_status", health.DivergenceStatus.ToString());
            PostgresqlCommandHelper.Add(cmd, "@working_tree_status", health.WorkingTreeStatus.ToString());
            PostgresqlCommandHelper.Add(cmd, "@branch_status", health.BranchStatus.ToString());
            PostgresqlCommandHelper.Add(cmd, "@readiness_status", health.ReadinessStatus.ToString());
            PostgresqlCommandHelper.Add(cmd, "@mission_outcome_status", health.MissionOutcomeStatus.ToString());
            PostgresqlCommandHelper.Add(cmd, "@last_check_run_status", health.LastCheckRunStatus);
            PostgresqlCommandHelper.Add(cmd, "@has_ci_config", health.HasCiConfig);
            PostgresqlCommandHelper.Add(cmd, "@has_license", health.HasLicense);
            PostgresqlCommandHelper.Add(cmd, "@has_readme", health.HasReadme);
            PostgresqlCommandHelper.Add(cmd, "@readiness_error_count", health.ReadinessErrorCount);
            PostgresqlCommandHelper.Add(cmd, "@recent_mission_failure_count", health.RecentMissionFailureCount);
            PostgresqlCommandHelper.Add(cmd, "@manifest_hash", health.ManifestHash);
            PostgresqlCommandHelper.AddDate(cmd, "@dependencies_evaluated_utc", health.DependenciesEvaluatedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@created_utc", health.CreatedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", health.LastUpdateUtc);
        }

        private static VesselHealth FromRow(NpgsqlDataReader reader)
        {
            VesselHealth health = new VesselHealth();
            health.Id = PostgresqlCommandHelper.ReadString(reader["id"]);
            health.TenantId = PostgresqlCommandHelper.ReadString(reader["tenant_id"]);
            health.VesselId = PostgresqlCommandHelper.ReadString(reader["vessel_id"]) ?? String.Empty;
            health.CreatedUtc = PostgresqlCommandHelper.ReadDate(reader["created_utc"]);
            health.LastUpdateUtc = PostgresqlCommandHelper.ReadDate(reader["last_update_utc"]);
            ReadMeasurements(reader, health);
            return health;
        }

        private static VesselHealth FromJoinedRow(NpgsqlDataReader reader)
        {
            VesselHealth health = new VesselHealth();
            health.Id = PostgresqlCommandHelper.ReadString(reader["id"]);
            health.TenantId = PostgresqlCommandHelper.ReadString(reader["v_tenant_id"]);
            health.VesselId = PostgresqlCommandHelper.ReadString(reader["v_id"]) ?? String.Empty;
            health.VesselName = PostgresqlCommandHelper.ReadString(reader["v_name"]);
            health.FleetId = PostgresqlCommandHelper.ReadString(reader["v_fleet_id"]);
            health.FleetName = PostgresqlCommandHelper.ReadString(reader["f_name"]);
            DateTime? created = PostgresqlCommandHelper.ReadNullableDate(reader["created_utc"]);
            DateTime? updated = PostgresqlCommandHelper.ReadNullableDate(reader["last_update_utc"]);
            if (created.HasValue) health.CreatedUtc = created.Value;
            if (updated.HasValue) health.LastUpdateUtc = updated.Value;
            ReadMeasurements(reader, health);
            return health;
        }

        private static void ReadMeasurements(NpgsqlDataReader reader, VesselHealth health)
        {
            health.OverallStatus = PostgresqlCommandHelper.ReadEnum(reader["overall_status"], VesselHealthStatusEnum.Unknown);
            health.EvaluatedUtc = PostgresqlCommandHelper.ReadNullableDate(reader["evaluated_utc"]);
            health.EvaluationDurationMs = PostgresqlCommandHelper.ReadNullableLong(reader["evaluation_duration_ms"]);
            health.ErrorCode = PostgresqlCommandHelper.ReadString(reader["error_code"]);
            health.EvaluatedPath = PostgresqlCommandHelper.ReadString(reader["evaluated_path"]);
            health.CurrentBranch = PostgresqlCommandHelper.ReadString(reader["current_branch"]);
            health.IsDirty = PostgresqlCommandHelper.ReadNullableBool(reader["is_dirty"]);
            health.UntrackedCount = PostgresqlCommandHelper.ReadNullableInt(reader["untracked_count"]);
            health.AheadOfDefault = PostgresqlCommandHelper.ReadNullableInt(reader["ahead_of_default"]);
            health.BehindDefault = PostgresqlCommandHelper.ReadNullableInt(reader["behind_default"]);
            health.AheadOfUpstream = PostgresqlCommandHelper.ReadNullableInt(reader["ahead_of_upstream"]);
            health.BehindUpstream = PostgresqlCommandHelper.ReadNullableInt(reader["behind_upstream"]);
            health.LastCommitUtc = PostgresqlCommandHelper.ReadNullableDate(reader["last_commit_utc"]);
            health.BranchCount = PostgresqlCommandHelper.ReadNullableInt(reader["branch_count"]);
            health.StaleBranchCount = PostgresqlCommandHelper.ReadNullableInt(reader["stale_branch_count"]);
            health.ArmadaBranchCount = PostgresqlCommandHelper.ReadNullableInt(reader["armada_branch_count"]);
            health.PrimaryLanguage = PostgresqlCommandHelper.ReadString(reader["primary_language"]);
            health.ProjectCount = PostgresqlCommandHelper.ReadNullableInt(reader["project_count"]);
            health.OutdatedCount = PostgresqlCommandHelper.ReadNullableInt(reader["outdated_count"]);
            health.OutdatedMajorCount = PostgresqlCommandHelper.ReadNullableInt(reader["outdated_major_count"]);
            health.VulnerableCount = PostgresqlCommandHelper.ReadNullableInt(reader["vulnerable_count"]);
            health.MaxVulnerabilitySeverity = PostgresqlCommandHelper.ReadEnum(reader["max_vulnerability_severity"], VulnerabilitySeverityEnum.None);
            health.DependencyStatus = PostgresqlCommandHelper.ReadEnum(reader["dependency_status"], VesselHealthStatusEnum.Unknown);
            health.VulnerabilityStatus = PostgresqlCommandHelper.ReadEnum(reader["vulnerability_status"], VesselHealthStatusEnum.Unknown);
            health.TestInfraStatus = PostgresqlCommandHelper.ReadEnum(reader["test_infra_status"], VesselHealthStatusEnum.Unknown);
            health.CiStatus = PostgresqlCommandHelper.ReadEnum(reader["ci_status"], VesselHealthStatusEnum.Unknown);
            health.DivergenceStatus = PostgresqlCommandHelper.ReadEnum(reader["divergence_status"], VesselHealthStatusEnum.Unknown);
            health.WorkingTreeStatus = PostgresqlCommandHelper.ReadEnum(reader["working_tree_status"], VesselHealthStatusEnum.Unknown);
            health.BranchStatus = PostgresqlCommandHelper.ReadEnum(reader["branch_status"], VesselHealthStatusEnum.Unknown);
            health.ReadinessStatus = PostgresqlCommandHelper.ReadEnum(reader["readiness_status"], VesselHealthStatusEnum.Unknown);
            health.MissionOutcomeStatus = PostgresqlCommandHelper.ReadEnum(reader["mission_outcome_status"], VesselHealthStatusEnum.Unknown);
            health.LastCheckRunStatus = PostgresqlCommandHelper.ReadString(reader["last_check_run_status"]);
            health.HasCiConfig = PostgresqlCommandHelper.ReadNullableBool(reader["has_ci_config"]);
            health.HasLicense = PostgresqlCommandHelper.ReadNullableBool(reader["has_license"]);
            health.HasReadme = PostgresqlCommandHelper.ReadNullableBool(reader["has_readme"]);
            health.ReadinessErrorCount = PostgresqlCommandHelper.ReadNullableInt(reader["readiness_error_count"]);
            health.RecentMissionFailureCount = PostgresqlCommandHelper.ReadNullableInt(reader["recent_mission_failure_count"]);
            health.ManifestHash = PostgresqlCommandHelper.ReadString(reader["manifest_hash"]);
            health.DependenciesEvaluatedUtc = PostgresqlCommandHelper.ReadNullableDate(reader["dependencies_evaluated_utc"]);
        }

        private static VesselHealthSummary SummaryFromReader(NpgsqlDataReader reader)
        {
            VesselHealthSummary summary = new VesselHealthSummary();
            summary.TotalVessels = PostgresqlCommandHelper.ReadNullableLong(reader["total_vessels"]) ?? 0;
            summary.NotEvaluated = PostgresqlCommandHelper.ReadNullableLong(reader["not_evaluated"]) ?? 0;
            summary.Pass = PostgresqlCommandHelper.ReadNullableLong(reader["pass_count"]) ?? 0;
            summary.Warn = PostgresqlCommandHelper.ReadNullableLong(reader["warn_count"]) ?? 0;
            summary.Fail = PostgresqlCommandHelper.ReadNullableLong(reader["fail_count"]) ?? 0;
            summary.Unknown = PostgresqlCommandHelper.ReadNullableLong(reader["unknown_count"]) ?? 0;
            summary.NotApplicable = PostgresqlCommandHelper.ReadNullableLong(reader["not_applicable_count"]) ?? 0;
            summary.OutdatedMajorVessels = PostgresqlCommandHelper.ReadNullableLong(reader["outdated_major_vessels"]) ?? 0;
            summary.HighOrCriticalVulnerabilityVessels = PostgresqlCommandHelper.ReadNullableLong(reader["high_critical_vessels"]) ?? 0;
            return summary;
        }

        #endregion
    }
}
