namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of deployment persistence.
    /// </summary>
    public class DeploymentMethods : IDeploymentMethods
    {
        private readonly string _ConnectionString;
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DeploymentMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        /// <inheritdoc />
        public async Task<Deployment> CreateAsync(Deployment deployment, CancellationToken token = default)
        {
            if (deployment == null) throw new ArgumentNullException(nameof(deployment));
            deployment.LastUpdateUtc = DateTime.UtcNow;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO deployments
                        (id, tenant_id, user_id, vessel_id, workflow_profile_id, environment_id, environment_name, release_id, mission_id, voyage_id,
                         title, source_ref, summary, notes, status, verification_status, approval_required, approved_by_user_id, approved_utc,
                         approval_comment, deploy_check_run_id, smoke_test_check_run_id, health_check_run_id, deployment_verification_check_run_id,
                         rollback_check_run_id, rollback_verification_check_run_id, check_run_ids_json, request_history_summary_json,
                         created_utc, started_utc, completed_utc, verified_utc, rolled_back_utc, monitoring_window_ends_utc,
                         last_monitored_utc, last_regression_alert_utc, latest_monitoring_summary, monitoring_failure_count, last_update_utc)
                        VALUES
                        (@id, @tenant_id, @user_id, @vessel_id, @workflow_profile_id, @environment_id, @environment_name, @release_id, @mission_id, @voyage_id,
                         @title, @source_ref, @summary, @notes, @status, @verification_status, @approval_required, @approved_by_user_id, @approved_utc,
                         @approval_comment, @deploy_check_run_id, @smoke_test_check_run_id, @health_check_run_id, @deployment_verification_check_run_id,
                         @rollback_check_run_id, @rollback_verification_check_run_id, @check_run_ids_json, @request_history_summary_json,
                         @created_utc, @started_utc, @completed_utc, @verified_utc, @rolled_back_utc, @monitoring_window_ends_utc,
                         @last_monitored_utc, @last_regression_alert_utc, @latest_monitoring_summary, @monitoring_failure_count, @last_update_utc);";
                    AddParameters(cmd, deployment);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return deployment;
        }

        /// <inheritdoc />
        public async Task<Deployment?> ReadAsync(string id, DeploymentQuery? query = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string> { "id = @id" };
                    List<MySqlParameter> parameters = new List<MySqlParameter> { new MySqlParameter("@id", id) };
                    ApplyQueryFilters(query, conditions, parameters);
                    cmd.CommandText = "SELECT * FROM deployments WHERE " + String.Join(" AND ", conditions) + " LIMIT 1;";
                    foreach (MySqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return FromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<Deployment> UpdateAsync(Deployment deployment, CancellationToken token = default)
        {
            if (deployment == null) throw new ArgumentNullException(nameof(deployment));
            deployment.LastUpdateUtc = DateTime.UtcNow;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE deployments SET
                        tenant_id = @tenant_id,
                        user_id = @user_id,
                        vessel_id = @vessel_id,
                        workflow_profile_id = @workflow_profile_id,
                        environment_id = @environment_id,
                        environment_name = @environment_name,
                        release_id = @release_id,
                        mission_id = @mission_id,
                        voyage_id = @voyage_id,
                        title = @title,
                        source_ref = @source_ref,
                        summary = @summary,
                        notes = @notes,
                        status = @status,
                        verification_status = @verification_status,
                        approval_required = @approval_required,
                        approved_by_user_id = @approved_by_user_id,
                        approved_utc = @approved_utc,
                        approval_comment = @approval_comment,
                        deploy_check_run_id = @deploy_check_run_id,
                        smoke_test_check_run_id = @smoke_test_check_run_id,
                        health_check_run_id = @health_check_run_id,
                        deployment_verification_check_run_id = @deployment_verification_check_run_id,
                        rollback_check_run_id = @rollback_check_run_id,
                        rollback_verification_check_run_id = @rollback_verification_check_run_id,
                        check_run_ids_json = @check_run_ids_json,
                        request_history_summary_json = @request_history_summary_json,
                        started_utc = @started_utc,
                        completed_utc = @completed_utc,
                        verified_utc = @verified_utc,
                        rolled_back_utc = @rolled_back_utc,
                        monitoring_window_ends_utc = @monitoring_window_ends_utc,
                        last_monitored_utc = @last_monitored_utc,
                        last_regression_alert_utc = @last_regression_alert_utc,
                        latest_monitoring_summary = @latest_monitoring_summary,
                        monitoring_failure_count = @monitoring_failure_count,
                        last_update_utc = @last_update_utc
                        WHERE id = @id;";
                    AddParameters(cmd, deployment);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return deployment;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, DeploymentQuery? query = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string> { "id = @id" };
                    List<MySqlParameter> parameters = new List<MySqlParameter> { new MySqlParameter("@id", id) };
                    ApplyQueryFilters(query, conditions, parameters);
                    cmd.CommandText = "DELETE FROM deployments WHERE " + String.Join(" AND ", conditions) + ";";
                    foreach (MySqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Deployment>> EnumerateAsync(DeploymentQuery query, CancellationToken token = default)
        {
            query ??= new DeploymentQuery();
            int pageSize = query.PageSize <= 0 ? 25 : query.PageSize;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<MySqlParameter> parameters = new List<MySqlParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

                long totalCount;
                using (MySqlCommand countCmd = conn.CreateCommand())
                {
                    countCmd.CommandText = "SELECT COUNT(*) FROM deployments" + whereClause + ";";
                    foreach (MySqlParameter parameter in parameters) countCmd.Parameters.Add(CloneParameter(parameter));
                    totalCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                List<Deployment> results = new List<Deployment>();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM deployments" + whereClause
                        + " ORDER BY COALESCE(completed_utc, started_utc, last_update_utc) DESC, created_utc DESC, id DESC LIMIT " + pageSize + " OFFSET " + query.Offset + ";";
                    foreach (MySqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(FromReader(reader));
                    }
                }

                return new EnumerationResult<Deployment>
                {
                    PageNumber = query.PageNumber,
                    PageSize = pageSize,
                    TotalRecords = totalCount,
                    TotalPages = pageSize > 0 ? (int)Math.Ceiling((double)totalCount / pageSize) : 0,
                    Objects = results
                };
            }
        }

        /// <inheritdoc />
        public async Task<List<Deployment>> EnumerateAllAsync(DeploymentQuery query, CancellationToken token = default)
        {
            query ??= new DeploymentQuery();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<MySqlParameter> parameters = new List<MySqlParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

                List<Deployment> results = new List<Deployment>();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM deployments" + whereClause + " ORDER BY COALESCE(completed_utc, started_utc, last_update_utc) DESC, created_utc DESC, id DESC;";
                    foreach (MySqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(FromReader(reader));
                    }
                }

                return results;
            }
        }

        private static void ApplyQueryFilters(DeploymentQuery? query, List<string> conditions, List<MySqlParameter> parameters)
        {
            if (query == null) return;

            if (!String.IsNullOrWhiteSpace(query.TenantId))
            {
                conditions.Add("tenant_id = @tenant_id");
                parameters.Add(new MySqlParameter("@tenant_id", query.TenantId));
            }
            if (!String.IsNullOrWhiteSpace(query.UserId))
            {
                conditions.Add("user_id = @user_id");
                parameters.Add(new MySqlParameter("@user_id", query.UserId));
            }
            if (!String.IsNullOrWhiteSpace(query.VesselId))
            {
                conditions.Add("vessel_id = @vessel_id");
                parameters.Add(new MySqlParameter("@vessel_id", query.VesselId));
            }
            if (!String.IsNullOrWhiteSpace(query.WorkflowProfileId))
            {
                conditions.Add("workflow_profile_id = @workflow_profile_id");
                parameters.Add(new MySqlParameter("@workflow_profile_id", query.WorkflowProfileId));
            }
            if (!String.IsNullOrWhiteSpace(query.EnvironmentId))
            {
                conditions.Add("environment_id = @environment_id");
                parameters.Add(new MySqlParameter("@environment_id", query.EnvironmentId));
            }
            if (!String.IsNullOrWhiteSpace(query.EnvironmentName))
            {
                conditions.Add("environment_name = @environment_name");
                parameters.Add(new MySqlParameter("@environment_name", query.EnvironmentName));
            }
            if (!String.IsNullOrWhiteSpace(query.ReleaseId))
            {
                conditions.Add("release_id = @release_id");
                parameters.Add(new MySqlParameter("@release_id", query.ReleaseId));
            }
            if (!String.IsNullOrWhiteSpace(query.MissionId))
            {
                conditions.Add("mission_id = @mission_id");
                parameters.Add(new MySqlParameter("@mission_id", query.MissionId));
            }
            if (!String.IsNullOrWhiteSpace(query.VoyageId))
            {
                conditions.Add("voyage_id = @voyage_id");
                parameters.Add(new MySqlParameter("@voyage_id", query.VoyageId));
            }
            if (!String.IsNullOrWhiteSpace(query.CheckRunId))
            {
                conditions.Add("check_run_ids_json LIKE @check_run_like");
                parameters.Add(new MySqlParameter("@check_run_like", "%\"" + query.CheckRunId + "\"%"));
            }
            if (query.Status.HasValue)
            {
                conditions.Add("status = @status");
                parameters.Add(new MySqlParameter("@status", query.Status.Value.ToString()));
            }
            if (query.VerificationStatus.HasValue)
            {
                conditions.Add("verification_status = @verification_status");
                parameters.Add(new MySqlParameter("@verification_status", query.VerificationStatus.Value.ToString()));
            }
            if (!String.IsNullOrWhiteSpace(query.Search))
            {
                conditions.Add("(LOWER(title) LIKE @search OR LOWER(COALESCE(source_ref, '')) LIKE @search OR LOWER(COALESCE(summary, '')) LIKE @search OR LOWER(COALESCE(notes, '')) LIKE @search OR LOWER(COALESCE(environment_name, '')) LIKE @search)");
                parameters.Add(new MySqlParameter("@search", "%" + query.Search.ToLowerInvariant() + "%"));
            }
            if (query.FromUtc.HasValue)
            {
                conditions.Add("created_utc >= @from_utc");
                parameters.Add(new MySqlParameter("@from_utc", query.FromUtc.Value));
            }
            if (query.ToUtc.HasValue)
            {
                conditions.Add("created_utc <= @to_utc");
                parameters.Add(new MySqlParameter("@to_utc", query.ToUtc.Value));
            }
        }

        private static void AddParameters(MySqlCommand cmd, Deployment deployment)
        {
            cmd.Parameters.AddWithValue("@id", deployment.Id);
            cmd.Parameters.AddWithValue("@tenant_id", (object?)deployment.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_id", (object?)deployment.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vessel_id", (object?)deployment.VesselId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@workflow_profile_id", (object?)deployment.WorkflowProfileId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@environment_id", (object?)deployment.EnvironmentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@environment_name", (object?)deployment.EnvironmentName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@release_id", (object?)deployment.ReleaseId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@mission_id", (object?)deployment.MissionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@voyage_id", (object?)deployment.VoyageId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@title", deployment.Title);
            cmd.Parameters.AddWithValue("@source_ref", (object?)deployment.SourceRef ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@summary", (object?)deployment.Summary ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@notes", (object?)deployment.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@status", deployment.Status.ToString());
            cmd.Parameters.AddWithValue("@verification_status", deployment.VerificationStatus.ToString());
            cmd.Parameters.AddWithValue("@approval_required", deployment.ApprovalRequired);
            cmd.Parameters.AddWithValue("@approved_by_user_id", (object?)deployment.ApprovedByUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@approved_utc", (object?)deployment.ApprovedUtc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@approval_comment", (object?)deployment.ApprovalComment ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@deploy_check_run_id", (object?)deployment.DeployCheckRunId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@smoke_test_check_run_id", (object?)deployment.SmokeTestCheckRunId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@health_check_run_id", (object?)deployment.HealthCheckRunId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@deployment_verification_check_run_id", (object?)deployment.DeploymentVerificationCheckRunId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rollback_check_run_id", (object?)deployment.RollbackCheckRunId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rollback_verification_check_run_id", (object?)deployment.RollbackVerificationCheckRunId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@check_run_ids_json", JsonSerializer.Serialize(deployment.CheckRunIds ?? new List<string>(), _Json));
            cmd.Parameters.AddWithValue("@request_history_summary_json", deployment.RequestHistorySummary != null
                ? JsonSerializer.Serialize(deployment.RequestHistorySummary, _Json)
                : DBNull.Value);
            cmd.Parameters.AddWithValue("@created_utc", deployment.CreatedUtc);
            cmd.Parameters.AddWithValue("@started_utc", (object?)deployment.StartedUtc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@completed_utc", (object?)deployment.CompletedUtc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@verified_utc", (object?)deployment.VerifiedUtc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rolled_back_utc", (object?)deployment.RolledBackUtc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@monitoring_window_ends_utc", (object?)deployment.MonitoringWindowEndsUtc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@last_monitored_utc", (object?)deployment.LastMonitoredUtc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@last_regression_alert_utc", (object?)deployment.LastRegressionAlertUtc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@latest_monitoring_summary", (object?)deployment.LatestMonitoringSummary ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@monitoring_failure_count", deployment.MonitoringFailureCount);
            cmd.Parameters.AddWithValue("@last_update_utc", deployment.LastUpdateUtc);
        }

        private static Deployment FromReader(MySqlDataReader reader)
        {
            Deployment deployment = new Deployment
            {
                Id = reader["id"].ToString() ?? String.Empty,
                TenantId = MysqlDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = MysqlDatabaseDriver.NullableString(reader["user_id"]),
                VesselId = MysqlDatabaseDriver.NullableString(reader["vessel_id"]),
                WorkflowProfileId = MysqlDatabaseDriver.NullableString(reader["workflow_profile_id"]),
                EnvironmentId = MysqlDatabaseDriver.NullableString(reader["environment_id"]),
                EnvironmentName = MysqlDatabaseDriver.NullableString(reader["environment_name"]),
                ReleaseId = MysqlDatabaseDriver.NullableString(reader["release_id"]),
                MissionId = MysqlDatabaseDriver.NullableString(reader["mission_id"]),
                VoyageId = MysqlDatabaseDriver.NullableString(reader["voyage_id"]),
                Title = reader["title"].ToString() ?? String.Empty,
                SourceRef = MysqlDatabaseDriver.NullableString(reader["source_ref"]),
                Summary = MysqlDatabaseDriver.NullableString(reader["summary"]),
                Notes = MysqlDatabaseDriver.NullableString(reader["notes"]),
                ApprovalRequired = Convert.ToBoolean(reader["approval_required"]),
                ApprovedByUserId = MysqlDatabaseDriver.NullableString(reader["approved_by_user_id"]),
                ApprovedUtc = reader["approved_utc"] == DBNull.Value ? null : DateTime.SpecifyKind(Convert.ToDateTime(reader["approved_utc"]), DateTimeKind.Utc),
                ApprovalComment = MysqlDatabaseDriver.NullableString(reader["approval_comment"]),
                DeployCheckRunId = MysqlDatabaseDriver.NullableString(reader["deploy_check_run_id"]),
                SmokeTestCheckRunId = MysqlDatabaseDriver.NullableString(reader["smoke_test_check_run_id"]),
                HealthCheckRunId = MysqlDatabaseDriver.NullableString(reader["health_check_run_id"]),
                DeploymentVerificationCheckRunId = MysqlDatabaseDriver.NullableString(reader["deployment_verification_check_run_id"]),
                RollbackCheckRunId = MysqlDatabaseDriver.NullableString(reader["rollback_check_run_id"]),
                RollbackVerificationCheckRunId = MysqlDatabaseDriver.NullableString(reader["rollback_verification_check_run_id"]),
                CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc),
                StartedUtc = reader["started_utc"] == DBNull.Value ? null : DateTime.SpecifyKind(Convert.ToDateTime(reader["started_utc"]), DateTimeKind.Utc),
                CompletedUtc = reader["completed_utc"] == DBNull.Value ? null : DateTime.SpecifyKind(Convert.ToDateTime(reader["completed_utc"]), DateTimeKind.Utc),
                VerifiedUtc = reader["verified_utc"] == DBNull.Value ? null : DateTime.SpecifyKind(Convert.ToDateTime(reader["verified_utc"]), DateTimeKind.Utc),
                RolledBackUtc = reader["rolled_back_utc"] == DBNull.Value ? null : DateTime.SpecifyKind(Convert.ToDateTime(reader["rolled_back_utc"]), DateTimeKind.Utc),
                MonitoringWindowEndsUtc = reader["monitoring_window_ends_utc"] == DBNull.Value ? null : DateTime.SpecifyKind(Convert.ToDateTime(reader["monitoring_window_ends_utc"]), DateTimeKind.Utc),
                LastMonitoredUtc = reader["last_monitored_utc"] == DBNull.Value ? null : DateTime.SpecifyKind(Convert.ToDateTime(reader["last_monitored_utc"]), DateTimeKind.Utc),
                LastRegressionAlertUtc = reader["last_regression_alert_utc"] == DBNull.Value ? null : DateTime.SpecifyKind(Convert.ToDateTime(reader["last_regression_alert_utc"]), DateTimeKind.Utc),
                LatestMonitoringSummary = MysqlDatabaseDriver.NullableString(reader["latest_monitoring_summary"]),
                MonitoringFailureCount = reader["monitoring_failure_count"] == DBNull.Value ? 0 : Convert.ToInt32(reader["monitoring_failure_count"]),
                LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc)
            };

            if (Enum.TryParse(reader["status"].ToString(), true, out DeploymentStatusEnum status))
                deployment.Status = status;
            if (Enum.TryParse(reader["verification_status"].ToString(), true, out DeploymentVerificationStatusEnum verificationStatus))
                deployment.VerificationStatus = verificationStatus;

            deployment.CheckRunIds = Deserialize<List<string>>(MysqlDatabaseDriver.NullableString(reader["check_run_ids_json"])) ?? new List<string>();
            deployment.RequestHistorySummary = Deserialize<RequestHistorySummaryResult>(MysqlDatabaseDriver.NullableString(reader["request_history_summary_json"]));
            return deployment;
        }

        private static T? Deserialize<T>(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return default;
            try
            {
                return JsonSerializer.Deserialize<T>(json, _Json);
            }
            catch
            {
                return default;
            }
        }

        private static MySqlParameter CloneParameter(MySqlParameter parameter)
        {
            return new MySqlParameter(parameter.ParameterName, parameter.Value ?? DBNull.Value);
        }
    }
}
