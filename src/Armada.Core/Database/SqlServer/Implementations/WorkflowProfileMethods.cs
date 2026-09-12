namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// SQL Server implementation of workflow-profile persistence.
    /// </summary>
    public class WorkflowProfileMethods : IWorkflowProfileMethods
    {
        private readonly SqlServerDatabaseDriver _Driver;
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public WorkflowProfileMethods(SqlServerDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc />
        public async Task<WorkflowProfile> CreateAsync(WorkflowProfile profile, CancellationToken token = default)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            profile.LastUpdateUtc = DateTime.UtcNow;

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO workflow_profiles
                        (id, tenant_id, user_id, name, description, scope, ownership_scope, fleet_id, vessel_id, is_default, active,
                         language_hints_json, lint_command, build_command, unit_test_command, integration_test_command,
                         e2e_test_command, package_command, publish_artifact_command, release_versioning_command,
                         changelog_generation_command, migration_command, security_scan_command, performance_command,
                         deployment_verification_command, rollback_verification_command, required_secrets_json,
                         expected_artifacts_json, environments_json, created_utc, last_update_utc)
                        VALUES
                        (@id, @tenant_id, @user_id, @name, @description, @scope, @ownership_scope, @fleet_id, @vessel_id, @is_default, @active,
                         @language_hints_json, @lint_command, @build_command, @unit_test_command, @integration_test_command,
                         @e2e_test_command, @package_command, @publish_artifact_command, @release_versioning_command,
                         @changelog_generation_command, @migration_command, @security_scan_command, @performance_command,
                         @deployment_verification_command, @rollback_verification_command, @required_secrets_json,
                         @expected_artifacts_json, @environments_json, @created_utc, @last_update_utc);";
                    AddParameters(cmd, profile);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return profile;
        }

        /// <inheritdoc />
        public async Task<WorkflowProfile?> ReadAsync(string id, WorkflowProfileQuery? query = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string> { "id = @id" };
                    List<SqlParameter> parameters = new List<SqlParameter> { new SqlParameter("@id", id) };
                    ApplyQueryFilters(query, conditions, parameters);
                    cmd.CommandText = "SELECT TOP 1 * FROM workflow_profiles WHERE " + String.Join(" AND ", conditions) + ";";
                    foreach (SqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return FromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<WorkflowProfile> UpdateAsync(WorkflowProfile profile, CancellationToken token = default)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            profile.LastUpdateUtc = DateTime.UtcNow;

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE workflow_profiles SET
                        tenant_id = @tenant_id,
                        user_id = @user_id,
                        name = @name,
                        description = @description,
                        scope = @scope,
                        ownership_scope = @ownership_scope,
                        fleet_id = @fleet_id,
                        vessel_id = @vessel_id,
                        is_default = @is_default,
                        active = @active,
                        language_hints_json = @language_hints_json,
                        lint_command = @lint_command,
                        build_command = @build_command,
                        unit_test_command = @unit_test_command,
                        integration_test_command = @integration_test_command,
                        e2e_test_command = @e2e_test_command,
                        package_command = @package_command,
                        publish_artifact_command = @publish_artifact_command,
                        release_versioning_command = @release_versioning_command,
                        changelog_generation_command = @changelog_generation_command,
                        migration_command = @migration_command,
                        security_scan_command = @security_scan_command,
                        performance_command = @performance_command,
                        deployment_verification_command = @deployment_verification_command,
                        rollback_verification_command = @rollback_verification_command,
                        required_secrets_json = @required_secrets_json,
                        expected_artifacts_json = @expected_artifacts_json,
                        environments_json = @environments_json,
                        last_update_utc = @last_update_utc
                        WHERE id = @id;";
                    AddParameters(cmd, profile);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return profile;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, WorkflowProfileQuery? query = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string> { "id = @id" };
                    List<SqlParameter> parameters = new List<SqlParameter> { new SqlParameter("@id", id) };
                    ApplyQueryFilters(query, conditions, parameters);
                    cmd.CommandText = "DELETE FROM workflow_profiles WHERE " + String.Join(" AND ", conditions) + ";";
                    foreach (SqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<WorkflowProfile>> EnumerateAsync(WorkflowProfileQuery query, CancellationToken token = default)
        {
            query ??= new WorkflowProfileQuery();
            int pageSize = query.PageSize <= 0 ? 25 : query.PageSize;

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<SqlParameter> parameters = new List<SqlParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

                long totalCount;
                using (SqlCommand countCmd = conn.CreateCommand())
                {
                    countCmd.CommandText = "SELECT COUNT(*) FROM workflow_profiles" + whereClause + ";";
                    foreach (SqlParameter parameter in parameters) countCmd.Parameters.Add(CloneParameter(parameter));
                    totalCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                List<WorkflowProfile> results = new List<WorkflowProfile>();
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM workflow_profiles" + whereClause
                        + " ORDER BY is_default DESC, last_update_utc DESC, name ASC OFFSET @offset ROWS FETCH NEXT @page_size ROWS ONLY;";
                    foreach (SqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    cmd.Parameters.AddWithValue("@offset", query.Offset);
                    cmd.Parameters.AddWithValue("@page_size", pageSize);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(FromReader(reader));
                    }
                }

                return new EnumerationResult<WorkflowProfile>
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
        public async Task<List<WorkflowProfile>> EnumerateAllAsync(WorkflowProfileQuery query, CancellationToken token = default)
        {
            query ??= new WorkflowProfileQuery();

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string>();
                    List<SqlParameter> parameters = new List<SqlParameter>();
                    ApplyQueryFilters(query, conditions, parameters);
                    string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;
                    cmd.CommandText = "SELECT * FROM workflow_profiles" + whereClause + " ORDER BY is_default DESC, last_update_utc DESC, name ASC;";
                    foreach (SqlParameter parameter in parameters) cmd.Parameters.Add(parameter);

                    List<WorkflowProfile> results = new List<WorkflowProfile>();
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(FromReader(reader));
                    }

                    return results;
                }
            }
        }

        private static void ApplyQueryFilters(WorkflowProfileQuery? query, List<string> conditions, List<SqlParameter> parameters)
        {
            if (query == null) return;

            if (!String.IsNullOrWhiteSpace(query.TenantId))
            {
                conditions.Add("tenant_id = @tenant_id");
                parameters.Add(new SqlParameter("@tenant_id", query.TenantId));
            }
            if (!String.IsNullOrWhiteSpace(query.UserId))
            {
                conditions.Add("user_id = @user_id");
                parameters.Add(new SqlParameter("@user_id", query.UserId));
            }
            if (query.Scope.HasValue)
            {
                conditions.Add("scope = @scope");
                parameters.Add(new SqlParameter("@scope", query.Scope.Value.ToString()));
            }
            if (!String.IsNullOrWhiteSpace(query.FleetId))
            {
                conditions.Add("fleet_id = @fleet_id");
                parameters.Add(new SqlParameter("@fleet_id", query.FleetId));
            }
            if (!String.IsNullOrWhiteSpace(query.VesselId))
            {
                conditions.Add("vessel_id = @vessel_id");
                parameters.Add(new SqlParameter("@vessel_id", query.VesselId));
            }
            if (!String.IsNullOrWhiteSpace(query.Search))
            {
                conditions.Add("(LOWER(name) LIKE @search OR LOWER(COALESCE(description, '')) LIKE @search)");
                parameters.Add(new SqlParameter("@search", "%" + query.Search.ToLowerInvariant() + "%"));
            }
            if (query.Active.HasValue)
            {
                conditions.Add("active = @active");
                parameters.Add(new SqlParameter("@active", query.Active.Value));
            }
            if (query.FromUtc.HasValue)
            {
                conditions.Add("created_utc >= @from_utc");
                parameters.Add(new SqlParameter("@from_utc", SqlServerDatabaseDriver.ToIso8601(query.FromUtc.Value)));
            }
            if (query.ToUtc.HasValue)
            {
                conditions.Add("created_utc <= @to_utc");
                parameters.Add(new SqlParameter("@to_utc", SqlServerDatabaseDriver.ToIso8601(query.ToUtc.Value)));
            }
        }

        private static void AddParameters(SqlCommand cmd, WorkflowProfile profile)
        {
            cmd.Parameters.AddWithValue("@id", profile.Id);
            cmd.Parameters.AddWithValue("@tenant_id", (object?)profile.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_id", (object?)profile.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@name", profile.Name);
            cmd.Parameters.AddWithValue("@description", (object?)profile.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@scope", profile.Scope.ToString());
            cmd.Parameters.AddWithValue("@ownership_scope", profile.OwnershipScope.ToString());
            cmd.Parameters.AddWithValue("@fleet_id", (object?)profile.FleetId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vessel_id", (object?)profile.VesselId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@is_default", profile.IsDefault);
            cmd.Parameters.AddWithValue("@active", profile.Active);
            cmd.Parameters.AddWithValue("@language_hints_json", Serialize(profile.LanguageHints));
            cmd.Parameters.AddWithValue("@lint_command", (object?)profile.LintCommand ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@build_command", (object?)profile.BuildCommand ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@unit_test_command", (object?)profile.UnitTestCommand ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@integration_test_command", (object?)profile.IntegrationTestCommand ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@e2e_test_command", (object?)profile.E2ETestCommand ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@package_command", (object?)profile.PackageCommand ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@publish_artifact_command", (object?)profile.PublishArtifactCommand ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@release_versioning_command", (object?)profile.ReleaseVersioningCommand ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@changelog_generation_command", (object?)profile.ChangelogGenerationCommand ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@migration_command", (object?)profile.MigrationCommand ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@security_scan_command", (object?)profile.SecurityScanCommand ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@performance_command", (object?)profile.PerformanceCommand ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@deployment_verification_command", (object?)profile.DeploymentVerificationCommand ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rollback_verification_command", (object?)profile.RollbackVerificationCommand ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@required_secrets_json", Serialize(profile.RequiredSecrets));
            cmd.Parameters.AddWithValue("@expected_artifacts_json", Serialize(profile.ExpectedArtifacts));
            cmd.Parameters.AddWithValue("@environments_json", Serialize(profile.Environments));
            cmd.Parameters.AddWithValue("@created_utc", SqlServerDatabaseDriver.ToIso8601(profile.CreatedUtc));
            cmd.Parameters.AddWithValue("@last_update_utc", SqlServerDatabaseDriver.ToIso8601(profile.LastUpdateUtc));
        }

        private static WorkflowProfile FromReader(SqlDataReader reader)
        {
            WorkflowProfile profile = new WorkflowProfile
            {
                Id = reader["id"].ToString() ?? String.Empty,
                TenantId = SqlServerDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = SqlServerDatabaseDriver.NullableString(reader["user_id"]),
                Name = reader["name"].ToString() ?? String.Empty,
                Description = SqlServerDatabaseDriver.NullableString(reader["description"]),
                FleetId = SqlServerDatabaseDriver.NullableString(reader["fleet_id"]),
                VesselId = SqlServerDatabaseDriver.NullableString(reader["vessel_id"]),
                IsDefault = Convert.ToBoolean(reader["is_default"]),
                Active = Convert.ToBoolean(reader["active"]),
                LintCommand = SqlServerDatabaseDriver.NullableString(reader["lint_command"]),
                BuildCommand = SqlServerDatabaseDriver.NullableString(reader["build_command"]),
                UnitTestCommand = SqlServerDatabaseDriver.NullableString(reader["unit_test_command"]),
                IntegrationTestCommand = SqlServerDatabaseDriver.NullableString(reader["integration_test_command"]),
                E2ETestCommand = SqlServerDatabaseDriver.NullableString(reader["e2e_test_command"]),
                PackageCommand = SqlServerDatabaseDriver.NullableString(reader["package_command"]),
                PublishArtifactCommand = SqlServerDatabaseDriver.NullableString(reader["publish_artifact_command"]),
                ReleaseVersioningCommand = SqlServerDatabaseDriver.NullableString(reader["release_versioning_command"]),
                ChangelogGenerationCommand = SqlServerDatabaseDriver.NullableString(reader["changelog_generation_command"]),
                MigrationCommand = SqlServerDatabaseDriver.NullableString(reader["migration_command"]),
                SecurityScanCommand = SqlServerDatabaseDriver.NullableString(reader["security_scan_command"]),
                PerformanceCommand = SqlServerDatabaseDriver.NullableString(reader["performance_command"]),
                DeploymentVerificationCommand = SqlServerDatabaseDriver.NullableString(reader["deployment_verification_command"]),
                RollbackVerificationCommand = SqlServerDatabaseDriver.NullableString(reader["rollback_verification_command"]),
                CreatedUtc = SqlServerDatabaseDriver.FromIso8601(reader["created_utc"].ToString()!),
                LastUpdateUtc = SqlServerDatabaseDriver.FromIso8601(reader["last_update_utc"].ToString()!)
            };

            if (Enum.TryParse(reader["scope"].ToString(), true, out WorkflowProfileScopeEnum scope))
                profile.Scope = scope;
                profile.OwnershipScope = System.Enum.TryParse<Armada.Core.Enums.ScopeEnum>(reader["ownership_scope"]?.ToString(), true, out Armada.Core.Enums.ScopeEnum __os) ? __os : Armada.Core.Enums.ScopeEnum.TenantWide;

            profile.LanguageHints = Deserialize<List<string>>(SqlServerDatabaseDriver.NullableString(reader["language_hints_json"])) ?? new List<string>();
            profile.RequiredSecrets = Deserialize<List<string>>(SqlServerDatabaseDriver.NullableString(reader["required_secrets_json"])) ?? new List<string>();
            profile.ExpectedArtifacts = Deserialize<List<string>>(SqlServerDatabaseDriver.NullableString(reader["expected_artifacts_json"])) ?? new List<string>();
            profile.Environments = Deserialize<List<WorkflowEnvironmentProfile>>(SqlServerDatabaseDriver.NullableString(reader["environments_json"])) ?? new List<WorkflowEnvironmentProfile>();
            return profile;
        }

        private static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value ?? Activator.CreateInstance<T>(), _Json);
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

        private static SqlParameter CloneParameter(SqlParameter parameter)
        {
            return new SqlParameter(parameter.ParameterName, parameter.Value ?? DBNull.Value);
        }
    }
}
