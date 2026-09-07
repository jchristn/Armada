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
    /// MySQL implementation of workflow-profile persistence.
    /// </summary>
    public class WorkflowProfileMethods : IWorkflowProfileMethods
    {
        private readonly string _ConnectionString;
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public WorkflowProfileMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        /// <inheritdoc />
        public async Task<WorkflowProfile> CreateAsync(WorkflowProfile profile, CancellationToken token = default)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            profile.LastUpdateUtc = DateTime.UtcNow;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string> { "id = @id" };
                    List<MySqlParameter> parameters = new List<MySqlParameter> { new MySqlParameter("@id", id) };
                    ApplyQueryFilters(query, conditions, parameters);
                    cmd.CommandText = "SELECT * FROM workflow_profiles WHERE " + String.Join(" AND ", conditions) + " LIMIT 1;";
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
        public async Task<WorkflowProfile> UpdateAsync(WorkflowProfile profile, CancellationToken token = default)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            profile.LastUpdateUtc = DateTime.UtcNow;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string> { "id = @id" };
                    List<MySqlParameter> parameters = new List<MySqlParameter> { new MySqlParameter("@id", id) };
                    ApplyQueryFilters(query, conditions, parameters);
                    cmd.CommandText = "DELETE FROM workflow_profiles WHERE " + String.Join(" AND ", conditions) + ";";
                    foreach (MySqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<WorkflowProfile>> EnumerateAsync(WorkflowProfileQuery query, CancellationToken token = default)
        {
            query ??= new WorkflowProfileQuery();
            int pageSize = query.PageSize <= 0 ? 25 : query.PageSize;
            int offset = query.PageNumber <= 1 ? 0 : (query.PageNumber - 1) * pageSize;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<MySqlParameter> parameters = new List<MySqlParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

                long totalCount;
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM workflow_profiles" + whereClause + ";";
                    foreach (MySqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                List<WorkflowProfile> results = new List<WorkflowProfile>();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM workflow_profiles" + whereClause
                        + " ORDER BY is_default DESC, last_update_utc DESC, name ASC LIMIT " + pageSize + " OFFSET " + offset + ";";
                    foreach (MySqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string>();
                    List<MySqlParameter> parameters = new List<MySqlParameter>();
                    ApplyQueryFilters(query, conditions, parameters);
                    string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;
                    cmd.CommandText = "SELECT * FROM workflow_profiles" + whereClause + " ORDER BY is_default DESC, last_update_utc DESC, name ASC;";
                    foreach (MySqlParameter parameter in parameters) cmd.Parameters.Add(parameter);

                    List<WorkflowProfile> results = new List<WorkflowProfile>();
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(FromReader(reader));
                    }

                    return results;
                }
            }
        }

        private static void ApplyQueryFilters(WorkflowProfileQuery? query, List<string> conditions, List<MySqlParameter> parameters)
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
            if (query.Scope.HasValue)
            {
                conditions.Add("scope = @scope");
                parameters.Add(new MySqlParameter("@scope", query.Scope.Value.ToString()));
            }
            if (!String.IsNullOrWhiteSpace(query.FleetId))
            {
                conditions.Add("fleet_id = @fleet_id");
                parameters.Add(new MySqlParameter("@fleet_id", query.FleetId));
            }
            if (!String.IsNullOrWhiteSpace(query.VesselId))
            {
                conditions.Add("vessel_id = @vessel_id");
                parameters.Add(new MySqlParameter("@vessel_id", query.VesselId));
            }
            if (!String.IsNullOrWhiteSpace(query.Search))
            {
                conditions.Add("(LOWER(name) LIKE @search OR LOWER(COALESCE(description, '')) LIKE @search)");
                parameters.Add(new MySqlParameter("@search", "%" + query.Search.ToLowerInvariant() + "%"));
            }
            if (query.Active.HasValue)
            {
                conditions.Add("active = @active");
                parameters.Add(new MySqlParameter("@active", query.Active.Value ? 1 : 0));
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

        private static void AddParameters(MySqlCommand cmd, WorkflowProfile profile)
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
            cmd.Parameters.AddWithValue("@is_default", profile.IsDefault ? 1 : 0);
            cmd.Parameters.AddWithValue("@active", profile.Active ? 1 : 0);
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
            cmd.Parameters.AddWithValue("@created_utc", profile.CreatedUtc);
            cmd.Parameters.AddWithValue("@last_update_utc", profile.LastUpdateUtc);
        }

        private static WorkflowProfile FromReader(MySqlDataReader reader)
        {
            WorkflowProfile profile = new WorkflowProfile
            {
                Id = reader["id"].ToString() ?? String.Empty,
                TenantId = MysqlDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = MysqlDatabaseDriver.NullableString(reader["user_id"]),
                Name = reader["name"].ToString() ?? String.Empty,
                Description = MysqlDatabaseDriver.NullableString(reader["description"]),
                FleetId = MysqlDatabaseDriver.NullableString(reader["fleet_id"]),
                VesselId = MysqlDatabaseDriver.NullableString(reader["vessel_id"]),
                IsDefault = Convert.ToInt64(reader["is_default"]) == 1,
                Active = Convert.ToInt64(reader["active"]) == 1,
                LintCommand = MysqlDatabaseDriver.NullableString(reader["lint_command"]),
                BuildCommand = MysqlDatabaseDriver.NullableString(reader["build_command"]),
                UnitTestCommand = MysqlDatabaseDriver.NullableString(reader["unit_test_command"]),
                IntegrationTestCommand = MysqlDatabaseDriver.NullableString(reader["integration_test_command"]),
                E2ETestCommand = MysqlDatabaseDriver.NullableString(reader["e2e_test_command"]),
                PackageCommand = MysqlDatabaseDriver.NullableString(reader["package_command"]),
                PublishArtifactCommand = MysqlDatabaseDriver.NullableString(reader["publish_artifact_command"]),
                ReleaseVersioningCommand = MysqlDatabaseDriver.NullableString(reader["release_versioning_command"]),
                ChangelogGenerationCommand = MysqlDatabaseDriver.NullableString(reader["changelog_generation_command"]),
                MigrationCommand = MysqlDatabaseDriver.NullableString(reader["migration_command"]),
                SecurityScanCommand = MysqlDatabaseDriver.NullableString(reader["security_scan_command"]),
                PerformanceCommand = MysqlDatabaseDriver.NullableString(reader["performance_command"]),
                DeploymentVerificationCommand = MysqlDatabaseDriver.NullableString(reader["deployment_verification_command"]),
                RollbackVerificationCommand = MysqlDatabaseDriver.NullableString(reader["rollback_verification_command"]),
                CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc),
                LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc)
            };

            if (Enum.TryParse(reader["scope"].ToString(), true, out WorkflowProfileScopeEnum scope))
                profile.Scope = scope;
                profile.OwnershipScope = System.Enum.TryParse<Armada.Core.Enums.ScopeEnum>(reader["ownership_scope"]?.ToString(), true, out Armada.Core.Enums.ScopeEnum __os) ? __os : Armada.Core.Enums.ScopeEnum.TenantWide;

            profile.LanguageHints = Deserialize<List<string>>(MysqlDatabaseDriver.NullableString(reader["language_hints_json"])) ?? new List<string>();
            profile.RequiredSecrets = Deserialize<List<string>>(MysqlDatabaseDriver.NullableString(reader["required_secrets_json"])) ?? new List<string>();
            profile.ExpectedArtifacts = Deserialize<List<string>>(MysqlDatabaseDriver.NullableString(reader["expected_artifacts_json"])) ?? new List<string>();
            profile.Environments = Deserialize<List<WorkflowEnvironmentProfile>>(MysqlDatabaseDriver.NullableString(reader["environments_json"])) ?? new List<WorkflowEnvironmentProfile>();
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

        private static MySqlParameter CloneParameter(MySqlParameter parameter)
        {
            return new MySqlParameter(parameter.ParameterName, parameter.Value);
        }
    }
}
