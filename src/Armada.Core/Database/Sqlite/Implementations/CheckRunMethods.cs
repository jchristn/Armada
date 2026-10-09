namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of structured check-run persistence.
    /// </summary>
    public class CheckRunMethods : ICheckRunMethods
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
        public CheckRunMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <inheritdoc />
        public async Task<CheckRun> CreateAsync(CheckRun checkRun, CancellationToken token = default)
        {
            if (checkRun == null) throw new ArgumentNullException(nameof(checkRun));
            checkRun.LastUpdateUtc = DateTime.UtcNow;

            using SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false);
            using SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO check_runs
                (id, tenant_id, user_id, workflow_profile_id, vessel_id, mission_id, voyage_id, deployment_id, label, check_type, status,
                 source, provider_name, external_id, external_url, environment_name, command, working_directory, branch_name, commit_hash, exit_code, output, summary,
                 test_summary_json, coverage_summary_json, artifacts_json, duration_ms, started_utc, completed_utc, created_utc, last_update_utc)
                VALUES
                (@id, @tenant_id, @user_id, @workflow_profile_id, @vessel_id, @mission_id, @voyage_id, @deployment_id, @label, @check_type, @status,
                 @source, @provider_name, @external_id, @external_url, @environment_name, @command, @working_directory, @branch_name, @commit_hash, @exit_code, @output, @summary,
                 @test_summary_json, @coverage_summary_json, @artifacts_json, @duration_ms, @started_utc, @completed_utc, @created_utc, @last_update_utc);";
            AddParameters(cmd, checkRun);
            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return checkRun;
        }

        /// <inheritdoc />
        public async Task<CheckRun?> ReadAsync(string id, CheckRunQuery? query = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            using SqliteCommand cmd = conn.CreateCommand();
            List<string> conditions = new List<string> { "id = @id" };
            List<SqliteParameter> parameters = new List<SqliteParameter> { new SqliteParameter("@id", id) };
            ApplyQueryFilters(query, conditions, parameters);
            cmd.CommandText = "SELECT * FROM check_runs WHERE " + String.Join(" AND ", conditions) + " LIMIT 1;";
            foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(parameter);

            using SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (await reader.ReadAsync(token).ConfigureAwait(false))
                return FromReader(reader);
            return null;
        }

        /// <inheritdoc />
        public async Task<CheckRun> UpdateAsync(CheckRun checkRun, CancellationToken token = default)
        {
            if (checkRun == null) throw new ArgumentNullException(nameof(checkRun));
            checkRun.LastUpdateUtc = DateTime.UtcNow;

            using SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false);
            using SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE check_runs SET
                tenant_id = @tenant_id,
                user_id = @user_id,
                workflow_profile_id = @workflow_profile_id,
                vessel_id = @vessel_id,
                mission_id = @mission_id,
                voyage_id = @voyage_id,
                deployment_id = @deployment_id,
                label = @label,
                check_type = @check_type,
                status = @status,
                source = @source,
                provider_name = @provider_name,
                external_id = @external_id,
                external_url = @external_url,
                environment_name = @environment_name,
                command = @command,
                working_directory = @working_directory,
                branch_name = @branch_name,
                commit_hash = @commit_hash,
                exit_code = @exit_code,
                output = @output,
                summary = @summary,
                test_summary_json = @test_summary_json,
                coverage_summary_json = @coverage_summary_json,
                artifacts_json = @artifacts_json,
                duration_ms = @duration_ms,
                started_utc = @started_utc,
                completed_utc = @completed_utc,
                last_update_utc = @last_update_utc
                WHERE id = @id;";
            AddParameters(cmd, checkRun);
            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return checkRun;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CheckRunQuery? query = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false);
            using SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            using SqliteCommand cmd = conn.CreateCommand();
            List<string> conditions = new List<string> { "id = @id" };
            List<SqliteParameter> parameters = new List<SqliteParameter> { new SqliteParameter("@id", id) };
            ApplyQueryFilters(query, conditions, parameters);
            cmd.CommandText = "DELETE FROM check_runs WHERE " + String.Join(" AND ", conditions) + ";";
            foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(parameter);
            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<CheckRun>> EnumerateAsync(CheckRunQuery query, CancellationToken token = default)
        {
            query ??= new CheckRunQuery();

            using SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);

            List<string> conditions = new List<string>();
            List<SqliteParameter> parameters = new List<SqliteParameter>();
            ApplyQueryFilters(query, conditions, parameters);
            string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            long totalCount;
            using (SqliteCommand countCmd = conn.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM check_runs" + whereClause + ";";
                foreach (SqliteParameter parameter in parameters) countCmd.Parameters.Add(new SqliteParameter(parameter.ParameterName, parameter.Value));
                totalCount = (long)(await countCmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
            }

            List<CheckRun> results = new List<CheckRun>();
            using (SqliteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM check_runs" + whereClause +
                    " ORDER BY created_utc DESC LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(new SqliteParameter(parameter.ParameterName, parameter.Value));
                using SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                    results.Add(FromReader(reader));
            }

            return new EnumerationResult<CheckRun>
            {
                PageNumber = query.PageNumber,
                PageSize = query.PageSize,
                TotalRecords = totalCount,
                TotalPages = query.PageSize > 0 ? (int)Math.Ceiling((double)totalCount / query.PageSize) : 0,
                Objects = results
            };
        }

        private static void ApplyQueryFilters(CheckRunQuery? query, List<string> conditions, List<SqliteParameter> parameters)
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
            if (!String.IsNullOrWhiteSpace(query.WorkflowProfileId))
            {
                conditions.Add("workflow_profile_id = @workflow_profile_id");
                parameters.Add(new SqliteParameter("@workflow_profile_id", query.WorkflowProfileId));
            }
            if (!String.IsNullOrWhiteSpace(query.VesselId))
            {
                conditions.Add("vessel_id = @vessel_id");
                parameters.Add(new SqliteParameter("@vessel_id", query.VesselId));
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
            if (!String.IsNullOrWhiteSpace(query.DeploymentId))
            {
                conditions.Add("deployment_id = @deployment_id");
                parameters.Add(new SqliteParameter("@deployment_id", query.DeploymentId));
            }
            if (query.Type.HasValue)
            {
                conditions.Add("check_type = @check_type");
                parameters.Add(new SqliteParameter("@check_type", query.Type.Value.ToString()));
            }
            if (query.Status.HasValue)
            {
                conditions.Add("status = @status");
                parameters.Add(new SqliteParameter("@status", query.Status.Value.ToString()));
            }
            if (query.Source.HasValue)
            {
                conditions.Add("source = @source");
                parameters.Add(new SqliteParameter("@source", query.Source.Value.ToString()));
            }
            if (!String.IsNullOrWhiteSpace(query.ProviderName))
            {
                conditions.Add("provider_name = @provider_name");
                parameters.Add(new SqliteParameter("@provider_name", query.ProviderName));
            }
            if (!String.IsNullOrWhiteSpace(query.ExternalId))
            {
                conditions.Add("external_id = @external_id");
                parameters.Add(new SqliteParameter("@external_id", query.ExternalId));
            }
            if (!String.IsNullOrWhiteSpace(query.EnvironmentName))
            {
                conditions.Add("environment_name = @environment_name");
                parameters.Add(new SqliteParameter("@environment_name", query.EnvironmentName));
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

        private static void AddParameters(SqliteCommand cmd, CheckRun checkRun)
        {
            cmd.Parameters.AddWithValue("@id", checkRun.Id);
            cmd.Parameters.AddWithValue("@tenant_id", (object?)checkRun.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_id", (object?)checkRun.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@workflow_profile_id", (object?)checkRun.WorkflowProfileId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vessel_id", (object?)checkRun.VesselId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@mission_id", (object?)checkRun.MissionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@voyage_id", (object?)checkRun.VoyageId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@deployment_id", (object?)checkRun.DeploymentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@label", (object?)checkRun.Label ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@check_type", checkRun.Type.ToString());
            cmd.Parameters.AddWithValue("@status", checkRun.Status.ToString());
            cmd.Parameters.AddWithValue("@source", checkRun.Source.ToString());
            cmd.Parameters.AddWithValue("@provider_name", (object?)checkRun.ProviderName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@external_id", (object?)checkRun.ExternalId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@external_url", (object?)checkRun.ExternalUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@environment_name", (object?)checkRun.EnvironmentName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@command", checkRun.Command);
            cmd.Parameters.AddWithValue("@working_directory", (object?)checkRun.WorkingDirectory ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@branch_name", (object?)checkRun.BranchName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@commit_hash", (object?)checkRun.CommitHash ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@exit_code", checkRun.ExitCode.HasValue ? checkRun.ExitCode.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@output", (object?)checkRun.Output ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@summary", (object?)checkRun.Summary ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@test_summary_json", checkRun.TestSummary != null ? JsonSerializer.Serialize(checkRun.TestSummary, _Json) : DBNull.Value);
            cmd.Parameters.AddWithValue("@coverage_summary_json", checkRun.CoverageSummary != null ? JsonSerializer.Serialize(checkRun.CoverageSummary, _Json) : DBNull.Value);
            cmd.Parameters.AddWithValue("@artifacts_json", JsonSerializer.Serialize(checkRun.Artifacts ?? new List<CheckRunArtifact>(), _Json));
            cmd.Parameters.AddWithValue("@duration_ms", checkRun.DurationMs.HasValue ? checkRun.DurationMs.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@started_utc", checkRun.StartedUtc.HasValue ? SqliteDatabaseDriver.ToIso8601(checkRun.StartedUtc.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@completed_utc", checkRun.CompletedUtc.HasValue ? SqliteDatabaseDriver.ToIso8601(checkRun.CompletedUtc.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@created_utc", SqliteDatabaseDriver.ToIso8601(checkRun.CreatedUtc));
            cmd.Parameters.AddWithValue("@last_update_utc", SqliteDatabaseDriver.ToIso8601(checkRun.LastUpdateUtc));
        }

        private static CheckRun FromReader(SqliteDataReader reader)
        {
            CheckRun run = new CheckRun
            {
                Id = reader["id"].ToString() ?? String.Empty,
                TenantId = SqliteDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = SqliteDatabaseDriver.NullableString(reader["user_id"]),
                WorkflowProfileId = SqliteDatabaseDriver.NullableString(reader["workflow_profile_id"]),
                VesselId = SqliteDatabaseDriver.NullableString(reader["vessel_id"]),
                MissionId = SqliteDatabaseDriver.NullableString(reader["mission_id"]),
                VoyageId = SqliteDatabaseDriver.NullableString(reader["voyage_id"]),
                DeploymentId = SqliteDatabaseDriver.NullableString(reader["deployment_id"]),
                Label = SqliteDatabaseDriver.NullableString(reader["label"]),
                ProviderName = SqliteDatabaseDriver.NullableString(reader["provider_name"]),
                ExternalId = SqliteDatabaseDriver.NullableString(reader["external_id"]),
                ExternalUrl = SqliteDatabaseDriver.NullableString(reader["external_url"]),
                EnvironmentName = SqliteDatabaseDriver.NullableString(reader["environment_name"]),
                Command = reader["command"].ToString() ?? String.Empty,
                WorkingDirectory = SqliteDatabaseDriver.NullableString(reader["working_directory"]),
                BranchName = SqliteDatabaseDriver.NullableString(reader["branch_name"]),
                CommitHash = SqliteDatabaseDriver.NullableString(reader["commit_hash"]),
                ExitCode = SqliteDatabaseDriver.NullableInt(reader["exit_code"]),
                Output = SqliteDatabaseDriver.NullableString(reader["output"]),
                Summary = SqliteDatabaseDriver.NullableString(reader["summary"]),
                DurationMs = SqliteDatabaseDriver.NullableLong(reader["duration_ms"]),
                StartedUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["started_utc"]),
                CompletedUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["completed_utc"]),
                CreatedUtc = SqliteDatabaseDriver.FromIso8601(reader["created_utc"].ToString()!),
                LastUpdateUtc = SqliteDatabaseDriver.FromIso8601(reader["last_update_utc"].ToString()!)
            };

            if (Enum.TryParse(reader["check_type"].ToString(), true, out CheckRunTypeEnum type))
                run.Type = type;
            if (Enum.TryParse(reader["status"].ToString(), true, out CheckRunStatusEnum status))
                run.Status = status;
            if (Enum.TryParse(reader["source"].ToString(), true, out CheckRunSourceEnum source))
                run.Source = source;

            string? testSummaryJson = SqliteDatabaseDriver.NullableString(reader["test_summary_json"]);
            if (!String.IsNullOrWhiteSpace(testSummaryJson))
            {
                try
                {
                    run.TestSummary = JsonSerializer.Deserialize<CheckRunTestSummary>(testSummaryJson, _Json);
                }
                catch
                {
                    run.TestSummary = null;
                }
            }

            string? coverageSummaryJson = SqliteDatabaseDriver.NullableString(reader["coverage_summary_json"]);
            if (!String.IsNullOrWhiteSpace(coverageSummaryJson))
            {
                try
                {
                    run.CoverageSummary = JsonSerializer.Deserialize<CheckRunCoverageSummary>(coverageSummaryJson, _Json);
                }
                catch
                {
                    run.CoverageSummary = null;
                }
            }

            string? artifactsJson = SqliteDatabaseDriver.NullableString(reader["artifacts_json"]);
            if (!String.IsNullOrWhiteSpace(artifactsJson))
            {
                try
                {
                    run.Artifacts = JsonSerializer.Deserialize<List<CheckRunArtifact>>(artifactsJson, _Json) ?? new List<CheckRunArtifact>();
                }
                catch
                {
                    run.Artifacts = new List<CheckRunArtifact>();
                }
            }

            return run;
        }
    }
}
