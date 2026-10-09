namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of deployment persistence.
    /// </summary>
    public class DeploymentMethods : IDeploymentMethods
    {
        private readonly SqliteDatabaseDriver _Driver;
        private readonly DatabaseSettings _Settings;
        private readonly LoggingModule _Logging;
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DeploymentMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <inheritdoc />
        public async Task<Deployment> CreateAsync(Deployment deployment, CancellationToken token = default)
        {
            if (deployment == null) throw new ArgumentNullException(nameof(deployment));
            deployment.LastUpdateUtc = DateTime.UtcNow;

            using SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false);
            using SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            using SqliteCommand cmd = conn.CreateCommand();
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
            return deployment;
        }

        /// <inheritdoc />
        public async Task<Deployment?> ReadAsync(string id, DeploymentQuery? query = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            using SqliteCommand cmd = conn.CreateCommand();

            List<string> conditions = new List<string> { "id = @id" };
            List<SqliteParameter> parameters = new List<SqliteParameter> { new SqliteParameter("@id", id) };
            ApplyQueryFilters(query, conditions, parameters, false);
            cmd.CommandText = "SELECT * FROM deployments WHERE " + String.Join(" AND ", conditions) + " LIMIT 1;";
            foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(parameter);

            using SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (await reader.ReadAsync(token).ConfigureAwait(false))
                return FromReader(reader);
            return null;
        }

        /// <inheritdoc />
        public async Task<Deployment> UpdateAsync(Deployment deployment, CancellationToken token = default)
        {
            if (deployment == null) throw new ArgumentNullException(nameof(deployment));
            deployment.LastUpdateUtc = DateTime.UtcNow;

            using SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false);
            using SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            using SqliteCommand cmd = conn.CreateCommand();
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
            return deployment;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, DeploymentQuery? query = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false);
            using SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            using SqliteCommand cmd = conn.CreateCommand();
            List<string> conditions = new List<string> { "id = @id" };
            List<SqliteParameter> parameters = new List<SqliteParameter> { new SqliteParameter("@id", id) };
            ApplyQueryFilters(query, conditions, parameters, false);
            cmd.CommandText = "DELETE FROM deployments WHERE " + String.Join(" AND ", conditions) + ";";
            foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(parameter);
            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Deployment>> EnumerateAsync(DeploymentQuery query, CancellationToken token = default)
        {
            query ??= new DeploymentQuery();

            using SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);

            List<string> conditions = new List<string>();
            List<SqliteParameter> parameters = new List<SqliteParameter>();
            ApplyQueryFilters(query, conditions, parameters, true);
            string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            long totalCount;
            using (SqliteCommand countCmd = conn.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM deployments" + whereClause + ";";
                foreach (SqliteParameter parameter in parameters) countCmd.Parameters.Add(new SqliteParameter(parameter.ParameterName, parameter.Value));
                totalCount = (long)(await countCmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
            }

            List<Deployment> results = new List<Deployment>();
            using (SqliteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM deployments" + whereClause
                    + " ORDER BY COALESCE(completed_utc, started_utc, last_update_utc) DESC, created_utc DESC"
                    + " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(new SqliteParameter(parameter.ParameterName, parameter.Value));
                using SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                    results.Add(FromReader(reader));
            }

            return new EnumerationResult<Deployment>
            {
                PageNumber = query.PageNumber,
                PageSize = query.PageSize,
                TotalRecords = totalCount,
                TotalPages = query.PageSize > 0 ? (int)Math.Ceiling((double)totalCount / query.PageSize) : 0,
                Objects = results
            };
        }

        /// <inheritdoc />
        public async Task<List<Deployment>> EnumerateAllAsync(DeploymentQuery query, CancellationToken token = default)
        {
            query ??= new DeploymentQuery();

            using SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            using SqliteCommand cmd = conn.CreateCommand();

            List<string> conditions = new List<string>();
            List<SqliteParameter> parameters = new List<SqliteParameter>();
            ApplyQueryFilters(query, conditions, parameters, false);
            string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;
            cmd.CommandText = "SELECT * FROM deployments" + whereClause + " ORDER BY COALESCE(completed_utc, started_utc, last_update_utc) DESC, created_utc DESC;";
            foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(parameter);

            List<Deployment> results = new List<Deployment>();
            using SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                results.Add(FromReader(reader));
            return results;
        }

        private static void ApplyQueryFilters(
            DeploymentQuery? query,
            List<string> conditions,
            List<SqliteParameter> parameters,
            bool includePaging)
        {
            if (query == null) return;

            if (!String.IsNullOrWhiteSpace(query.TenantId))
            {
                conditions.Add("tenant_id = @tenant_id");
                parameters.Add(new SqliteParameter("@tenant_id", query.TenantId));
            }
            if (!String.IsNullOrWhiteSpace(query.UserId))
            {
                conditions.Add("user_id = @user_id");
                parameters.Add(new SqliteParameter("@user_id", query.UserId));
            }
            if (!String.IsNullOrWhiteSpace(query.VesselId))
            {
                conditions.Add("vessel_id = @vessel_id");
                parameters.Add(new SqliteParameter("@vessel_id", query.VesselId));
            }
            if (!String.IsNullOrWhiteSpace(query.WorkflowProfileId))
            {
                conditions.Add("workflow_profile_id = @workflow_profile_id");
                parameters.Add(new SqliteParameter("@workflow_profile_id", query.WorkflowProfileId));
            }
            if (!String.IsNullOrWhiteSpace(query.EnvironmentId))
            {
                conditions.Add("environment_id = @environment_id");
                parameters.Add(new SqliteParameter("@environment_id", query.EnvironmentId));
            }
            if (!String.IsNullOrWhiteSpace(query.EnvironmentName))
            {
                conditions.Add("environment_name = @environment_name");
                parameters.Add(new SqliteParameter("@environment_name", query.EnvironmentName));
            }
            if (!String.IsNullOrWhiteSpace(query.ReleaseId))
            {
                conditions.Add("release_id = @release_id");
                parameters.Add(new SqliteParameter("@release_id", query.ReleaseId));
            }
            if (!String.IsNullOrWhiteSpace(query.MissionId))
            {
                conditions.Add("mission_id = @mission_id");
                parameters.Add(new SqliteParameter("@mission_id", query.MissionId));
            }
            if (!String.IsNullOrWhiteSpace(query.VoyageId))
            {
                conditions.Add("voyage_id = @voyage_id");
                parameters.Add(new SqliteParameter("@voyage_id", query.VoyageId));
            }
            if (!String.IsNullOrWhiteSpace(query.CheckRunId))
            {
                conditions.Add("check_run_ids_json LIKE @check_run_like");
                parameters.Add(new SqliteParameter("@check_run_like", "%\"" + query.CheckRunId + "\"%"));
            }
            if (query.Status.HasValue)
            {
                conditions.Add("status = @status");
                parameters.Add(new SqliteParameter("@status", query.Status.Value.ToString()));
            }
            if (query.VerificationStatus.HasValue)
            {
                conditions.Add("verification_status = @verification_status");
                parameters.Add(new SqliteParameter("@verification_status", query.VerificationStatus.Value.ToString()));
            }
            if (!String.IsNullOrWhiteSpace(query.Search))
            {
                conditions.Add("(LOWER(title) LIKE @search OR LOWER(COALESCE(source_ref, '')) LIKE @search OR LOWER(COALESCE(summary, '')) LIKE @search OR LOWER(COALESCE(notes, '')) LIKE @search OR LOWER(COALESCE(environment_name, '')) LIKE @search)");
                parameters.Add(new SqliteParameter("@search", "%" + query.Search.ToLowerInvariant() + "%"));
            }
            if (query.FromUtc.HasValue)
            {
                conditions.Add("created_utc >= @from_utc");
                parameters.Add(new SqliteParameter("@from_utc", SqliteDatabaseDriver.ToIso8601(query.FromUtc.Value)));
            }
            if (query.ToUtc.HasValue)
            {
                conditions.Add("created_utc <= @to_utc");
                parameters.Add(new SqliteParameter("@to_utc", SqliteDatabaseDriver.ToIso8601(query.ToUtc.Value)));
            }
        }

        private static void AddParameters(SqliteCommand cmd, Deployment deployment)
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
            cmd.Parameters.AddWithValue("@approval_required", deployment.ApprovalRequired ? 1 : 0);
            cmd.Parameters.AddWithValue("@approved_by_user_id", (object?)deployment.ApprovedByUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@approved_utc", deployment.ApprovedUtc.HasValue ? SqliteDatabaseDriver.ToIso8601(deployment.ApprovedUtc.Value) : DBNull.Value);
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
            cmd.Parameters.AddWithValue("@created_utc", SqliteDatabaseDriver.ToIso8601(deployment.CreatedUtc));
            cmd.Parameters.AddWithValue("@started_utc", deployment.StartedUtc.HasValue ? SqliteDatabaseDriver.ToIso8601(deployment.StartedUtc.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@completed_utc", deployment.CompletedUtc.HasValue ? SqliteDatabaseDriver.ToIso8601(deployment.CompletedUtc.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@verified_utc", deployment.VerifiedUtc.HasValue ? SqliteDatabaseDriver.ToIso8601(deployment.VerifiedUtc.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@rolled_back_utc", deployment.RolledBackUtc.HasValue ? SqliteDatabaseDriver.ToIso8601(deployment.RolledBackUtc.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@monitoring_window_ends_utc", deployment.MonitoringWindowEndsUtc.HasValue ? SqliteDatabaseDriver.ToIso8601(deployment.MonitoringWindowEndsUtc.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@last_monitored_utc", deployment.LastMonitoredUtc.HasValue ? SqliteDatabaseDriver.ToIso8601(deployment.LastMonitoredUtc.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@last_regression_alert_utc", deployment.LastRegressionAlertUtc.HasValue ? SqliteDatabaseDriver.ToIso8601(deployment.LastRegressionAlertUtc.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@latest_monitoring_summary", (object?)deployment.LatestMonitoringSummary ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@monitoring_failure_count", deployment.MonitoringFailureCount);
            cmd.Parameters.AddWithValue("@last_update_utc", SqliteDatabaseDriver.ToIso8601(deployment.LastUpdateUtc));
        }

        private static Deployment FromReader(SqliteDataReader reader)
        {
            Deployment deployment = new Deployment
            {
                Id = reader["id"].ToString() ?? String.Empty,
                TenantId = SqliteDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = SqliteDatabaseDriver.NullableString(reader["user_id"]),
                VesselId = SqliteDatabaseDriver.NullableString(reader["vessel_id"]),
                WorkflowProfileId = SqliteDatabaseDriver.NullableString(reader["workflow_profile_id"]),
                EnvironmentId = SqliteDatabaseDriver.NullableString(reader["environment_id"]),
                EnvironmentName = SqliteDatabaseDriver.NullableString(reader["environment_name"]),
                ReleaseId = SqliteDatabaseDriver.NullableString(reader["release_id"]),
                MissionId = SqliteDatabaseDriver.NullableString(reader["mission_id"]),
                VoyageId = SqliteDatabaseDriver.NullableString(reader["voyage_id"]),
                Title = reader["title"].ToString() ?? String.Empty,
                SourceRef = SqliteDatabaseDriver.NullableString(reader["source_ref"]),
                Summary = SqliteDatabaseDriver.NullableString(reader["summary"]),
                Notes = SqliteDatabaseDriver.NullableString(reader["notes"]),
                ApprovalRequired = Convert.ToInt64(reader["approval_required"]) == 1,
                ApprovedByUserId = SqliteDatabaseDriver.NullableString(reader["approved_by_user_id"]),
                ApprovedUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["approved_utc"]),
                ApprovalComment = SqliteDatabaseDriver.NullableString(reader["approval_comment"]),
                DeployCheckRunId = SqliteDatabaseDriver.NullableString(reader["deploy_check_run_id"]),
                SmokeTestCheckRunId = SqliteDatabaseDriver.NullableString(reader["smoke_test_check_run_id"]),
                HealthCheckRunId = SqliteDatabaseDriver.NullableString(reader["health_check_run_id"]),
                DeploymentVerificationCheckRunId = SqliteDatabaseDriver.NullableString(reader["deployment_verification_check_run_id"]),
                RollbackCheckRunId = SqliteDatabaseDriver.NullableString(reader["rollback_check_run_id"]),
                RollbackVerificationCheckRunId = SqliteDatabaseDriver.NullableString(reader["rollback_verification_check_run_id"]),
                CreatedUtc = SqliteDatabaseDriver.FromIso8601(reader["created_utc"].ToString()!),
                StartedUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["started_utc"]),
                CompletedUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["completed_utc"]),
                VerifiedUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["verified_utc"]),
                RolledBackUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["rolled_back_utc"]),
                MonitoringWindowEndsUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["monitoring_window_ends_utc"]),
                LastMonitoredUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["last_monitored_utc"]),
                LastRegressionAlertUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["last_regression_alert_utc"]),
                LatestMonitoringSummary = SqliteDatabaseDriver.NullableString(reader["latest_monitoring_summary"]),
                MonitoringFailureCount = Convert.ToInt32(reader["monitoring_failure_count"]),
                LastUpdateUtc = SqliteDatabaseDriver.FromIso8601(reader["last_update_utc"].ToString()!)
            };

            if (Enum.TryParse(reader["status"].ToString(), true, out DeploymentStatusEnum status))
                deployment.Status = status;
            if (Enum.TryParse(reader["verification_status"].ToString(), true, out DeploymentVerificationStatusEnum verificationStatus))
                deployment.VerificationStatus = verificationStatus;

            deployment.CheckRunIds = Deserialize<List<string>>(SqliteDatabaseDriver.NullableString(reader["check_run_ids_json"])) ?? new List<string>();
            deployment.RequestHistorySummary = Deserialize<RequestHistorySummaryResult>(SqliteDatabaseDriver.NullableString(reader["request_history_summary_json"]));
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
    }
}
