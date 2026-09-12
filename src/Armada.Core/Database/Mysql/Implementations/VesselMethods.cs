namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of vessel database operations.
    /// </summary>
    public class VesselMethods : IVesselMethods
    {
        #region Private-Members

        private string _ConnectionString;
        private static readonly string _Iso8601Format = "yyyy-MM-dd HH:mm:ss.ffffff";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with a MySQL connection string.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        public VesselMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a vessel.
        /// </summary>
        /// <param name="vessel">Vessel to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created vessel.</returns>
        public async Task<Vessel> CreateAsync(Vessel vessel, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            vessel.LastUpdateUtc = DateTime.UtcNow;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO vessels (id, tenant_id, user_id, fleet_id, name, repo_url, local_path, working_directory, preferred_harbor_id, required_capabilities, project_context, style_guide, enable_model_context, model_context, github_token_override, landing_mode, branch_cleanup_policy, require_passing_checks_to_land, allow_concurrent_missions, secret_scan_enabled, protected_path_patterns_json, private_identifier_denylist_json, auto_land_enabled, auto_land_max_files, auto_land_max_lines, auto_land_path_allow_globs_json, auto_land_path_deny_globs_json, definition_of_done_enabled, definition_of_done_build_command, definition_of_done_test_command, definition_of_done_timeout_seconds, default_pipeline_id, default_branch, protected_branch_patterns_json, release_branch_prefix, hotfix_branch_prefix, require_pull_request_for_protected_branches, require_merge_queue_for_release_branches, active, created_utc, last_update_utc)
                        VALUES (@id, @tenant_id, @user_id, @fleet_id, @name, @repo_url, @local_path, @working_directory, @preferred_harbor_id, @required_capabilities, @project_context, @style_guide, @enable_model_context, @model_context, @github_token_override, @landing_mode, @branch_cleanup_policy, @require_passing_checks_to_land, @allow_concurrent_missions, @secret_scan_enabled, @protected_path_patterns_json, @private_identifier_denylist_json, @auto_land_enabled, @auto_land_max_files, @auto_land_max_lines, @auto_land_path_allow_globs_json, @auto_land_path_deny_globs_json, @definition_of_done_enabled, @definition_of_done_build_command, @definition_of_done_test_command, @definition_of_done_timeout_seconds, @default_pipeline_id, @default_branch, @protected_branch_patterns_json, @release_branch_prefix, @hotfix_branch_prefix, @require_pull_request_for_protected_branches, @require_merge_queue_for_release_branches, @active, @created_utc, @last_update_utc);";
                    cmd.Parameters.AddWithValue("@id", vessel.Id);
                    cmd.Parameters.AddWithValue("@tenant_id", (object?)vessel.TenantId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@user_id", (object?)vessel.UserId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@fleet_id", (object?)vessel.FleetId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@name", vessel.Name);
                    cmd.Parameters.AddWithValue("@repo_url", (object?)vessel.RepoUrl ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@local_path", (object?)vessel.LocalPath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@working_directory", (object?)vessel.WorkingDirectory ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@preferred_harbor_id", (object?)vessel.PreferredHarborId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@required_capabilities", (object?)vessel.RequiredCapabilities ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@project_context", (object?)vessel.ProjectContext ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@style_guide", (object?)vessel.StyleGuide ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@enable_model_context", vessel.EnableModelContext ? 1 : 0);
                    cmd.Parameters.AddWithValue("@model_context", (object?)vessel.ModelContext ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@github_token_override", (object?)vessel.GitHubTokenOverride ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@landing_mode", vessel.LandingMode.HasValue ? vessel.LandingMode.Value.ToString() : DBNull.Value);
                    cmd.Parameters.AddWithValue("@branch_cleanup_policy", vessel.BranchCleanupPolicy.HasValue ? vessel.BranchCleanupPolicy.Value.ToString() : DBNull.Value);
                    cmd.Parameters.AddWithValue("@require_passing_checks_to_land", vessel.RequirePassingChecksToLand ? 1 : 0);
                    cmd.Parameters.AddWithValue("@allow_concurrent_missions", vessel.AllowConcurrentMissions ? 1 : 0);
                    cmd.Parameters.AddWithValue("@secret_scan_enabled", vessel.SecretScanEnabled ? 1 : 0);
                    cmd.Parameters.AddWithValue("@protected_path_patterns_json", JsonSerializer.Serialize(vessel.ProtectedPathPatterns ?? new List<string>()));
                    cmd.Parameters.AddWithValue("@private_identifier_denylist_json", JsonSerializer.Serialize(vessel.PrivateIdentifierDenylist ?? new List<string>()));
                    cmd.Parameters.AddWithValue("@auto_land_enabled", vessel.AutoLandEnabled ? 1 : 0);
                    cmd.Parameters.AddWithValue("@auto_land_max_files", vessel.AutoLandMaxFiles);
                    cmd.Parameters.AddWithValue("@auto_land_max_lines", vessel.AutoLandMaxLines);
                    cmd.Parameters.AddWithValue("@auto_land_path_allow_globs_json", JsonSerializer.Serialize(vessel.AutoLandPathAllowGlobs ?? new List<string>()));
                    cmd.Parameters.AddWithValue("@auto_land_path_deny_globs_json", JsonSerializer.Serialize(vessel.AutoLandPathDenyGlobs ?? new List<string>()));
                    cmd.Parameters.AddWithValue("@definition_of_done_enabled", vessel.DefinitionOfDoneEnabled ? 1 : 0);
                    cmd.Parameters.AddWithValue("@definition_of_done_build_command", (object?)vessel.DefinitionOfDoneBuildCommand ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@definition_of_done_test_command", (object?)vessel.DefinitionOfDoneTestCommand ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@definition_of_done_timeout_seconds", vessel.DefinitionOfDoneTimeoutSeconds);
                    cmd.Parameters.AddWithValue("@default_pipeline_id", (object?)vessel.DefaultPipelineId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@default_branch", vessel.DefaultBranch);
                    cmd.Parameters.AddWithValue("@protected_branch_patterns_json", JsonSerializer.Serialize(vessel.ProtectedBranchPatterns ?? new List<string>()));
                    cmd.Parameters.AddWithValue("@release_branch_prefix", vessel.ReleaseBranchPrefix);
                    cmd.Parameters.AddWithValue("@hotfix_branch_prefix", vessel.HotfixBranchPrefix);
                    cmd.Parameters.AddWithValue("@require_pull_request_for_protected_branches", vessel.RequirePullRequestForProtectedBranches ? 1 : 0);
                    cmd.Parameters.AddWithValue("@require_merge_queue_for_release_branches", vessel.RequireMergeQueueForReleaseBranches ? 1 : 0);
                    cmd.Parameters.AddWithValue("@active", vessel.Active ? 1 : 0);
                    cmd.Parameters.AddWithValue("@created_utc", ToIso8601(vessel.CreatedUtc));
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(vessel.LastUpdateUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return vessel;
        }

        /// <summary>
        /// Read a vessel by identifier.
        /// </summary>
        /// <param name="id">Vessel identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Vessel or null if not found.</returns>
        public async Task<Vessel?> ReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM vessels WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return VesselFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Read a vessel by name.
        /// </summary>
        /// <param name="name">Vessel name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Vessel or null if not found.</returns>
        public async Task<Vessel?> ReadByNameAsync(string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM vessels WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return VesselFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Update a vessel.
        /// </summary>
        /// <param name="vessel">Vessel with updated values.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated vessel.</returns>
        public async Task<Vessel> UpdateAsync(Vessel vessel, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            vessel.LastUpdateUtc = DateTime.UtcNow;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE vessels SET
                        tenant_id = @tenant_id,
                            user_id = @user_id,
                        fleet_id = @fleet_id,
                        name = @name,
                        repo_url = @repo_url,
                        local_path = @local_path,
                        working_directory = @working_directory,
                        preferred_harbor_id = @preferred_harbor_id,
                        required_capabilities = @required_capabilities,
                        project_context = @project_context,
                        style_guide = @style_guide,
                        enable_model_context = @enable_model_context,
                        model_context = @model_context,
                        github_token_override = @github_token_override,
                        landing_mode = @landing_mode,
                        branch_cleanup_policy = @branch_cleanup_policy,
                        require_passing_checks_to_land = @require_passing_checks_to_land,
                        allow_concurrent_missions = @allow_concurrent_missions,
                        secret_scan_enabled = @secret_scan_enabled,
                        protected_path_patterns_json = @protected_path_patterns_json,
                        private_identifier_denylist_json = @private_identifier_denylist_json,
                        auto_land_enabled = @auto_land_enabled,
                        auto_land_max_files = @auto_land_max_files,
                        auto_land_max_lines = @auto_land_max_lines,
                        auto_land_path_allow_globs_json = @auto_land_path_allow_globs_json,
                        auto_land_path_deny_globs_json = @auto_land_path_deny_globs_json,
                        definition_of_done_enabled = @definition_of_done_enabled,
                        definition_of_done_build_command = @definition_of_done_build_command,
                        definition_of_done_test_command = @definition_of_done_test_command,
                        definition_of_done_timeout_seconds = @definition_of_done_timeout_seconds,
                        default_pipeline_id = @default_pipeline_id,
                        default_branch = @default_branch,
                        protected_branch_patterns_json = @protected_branch_patterns_json,
                        release_branch_prefix = @release_branch_prefix,
                        hotfix_branch_prefix = @hotfix_branch_prefix,
                        require_pull_request_for_protected_branches = @require_pull_request_for_protected_branches,
                        require_merge_queue_for_release_branches = @require_merge_queue_for_release_branches,
                        active = @active,
                        last_update_utc = @last_update_utc
                        WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", vessel.Id);
                    cmd.Parameters.AddWithValue("@tenant_id", (object?)vessel.TenantId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@user_id", (object?)vessel.UserId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@fleet_id", (object?)vessel.FleetId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@name", vessel.Name);
                    cmd.Parameters.AddWithValue("@repo_url", (object?)vessel.RepoUrl ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@local_path", (object?)vessel.LocalPath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@working_directory", (object?)vessel.WorkingDirectory ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@preferred_harbor_id", (object?)vessel.PreferredHarborId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@required_capabilities", (object?)vessel.RequiredCapabilities ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@project_context", (object?)vessel.ProjectContext ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@style_guide", (object?)vessel.StyleGuide ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@enable_model_context", vessel.EnableModelContext ? 1 : 0);
                    cmd.Parameters.AddWithValue("@model_context", (object?)vessel.ModelContext ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@github_token_override", (object?)vessel.GitHubTokenOverride ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@landing_mode", vessel.LandingMode.HasValue ? vessel.LandingMode.Value.ToString() : DBNull.Value);
                    cmd.Parameters.AddWithValue("@branch_cleanup_policy", vessel.BranchCleanupPolicy.HasValue ? vessel.BranchCleanupPolicy.Value.ToString() : DBNull.Value);
                    cmd.Parameters.AddWithValue("@require_passing_checks_to_land", vessel.RequirePassingChecksToLand ? 1 : 0);
                    cmd.Parameters.AddWithValue("@allow_concurrent_missions", vessel.AllowConcurrentMissions ? 1 : 0);
                    cmd.Parameters.AddWithValue("@secret_scan_enabled", vessel.SecretScanEnabled ? 1 : 0);
                    cmd.Parameters.AddWithValue("@protected_path_patterns_json", JsonSerializer.Serialize(vessel.ProtectedPathPatterns ?? new List<string>()));
                    cmd.Parameters.AddWithValue("@private_identifier_denylist_json", JsonSerializer.Serialize(vessel.PrivateIdentifierDenylist ?? new List<string>()));
                    cmd.Parameters.AddWithValue("@auto_land_enabled", vessel.AutoLandEnabled ? 1 : 0);
                    cmd.Parameters.AddWithValue("@auto_land_max_files", vessel.AutoLandMaxFiles);
                    cmd.Parameters.AddWithValue("@auto_land_max_lines", vessel.AutoLandMaxLines);
                    cmd.Parameters.AddWithValue("@auto_land_path_allow_globs_json", JsonSerializer.Serialize(vessel.AutoLandPathAllowGlobs ?? new List<string>()));
                    cmd.Parameters.AddWithValue("@auto_land_path_deny_globs_json", JsonSerializer.Serialize(vessel.AutoLandPathDenyGlobs ?? new List<string>()));
                    cmd.Parameters.AddWithValue("@definition_of_done_enabled", vessel.DefinitionOfDoneEnabled ? 1 : 0);
                    cmd.Parameters.AddWithValue("@definition_of_done_build_command", (object?)vessel.DefinitionOfDoneBuildCommand ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@definition_of_done_test_command", (object?)vessel.DefinitionOfDoneTestCommand ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@definition_of_done_timeout_seconds", vessel.DefinitionOfDoneTimeoutSeconds);
                    cmd.Parameters.AddWithValue("@default_pipeline_id", (object?)vessel.DefaultPipelineId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@default_branch", vessel.DefaultBranch);
                    cmd.Parameters.AddWithValue("@protected_branch_patterns_json", JsonSerializer.Serialize(vessel.ProtectedBranchPatterns ?? new List<string>()));
                    cmd.Parameters.AddWithValue("@release_branch_prefix", vessel.ReleaseBranchPrefix);
                    cmd.Parameters.AddWithValue("@hotfix_branch_prefix", vessel.HotfixBranchPrefix);
                    cmd.Parameters.AddWithValue("@require_pull_request_for_protected_branches", vessel.RequirePullRequestForProtectedBranches ? 1 : 0);
                    cmd.Parameters.AddWithValue("@require_merge_queue_for_release_branches", vessel.RequireMergeQueueForReleaseBranches ? 1 : 0);
                    cmd.Parameters.AddWithValue("@active", vessel.Active ? 1 : 0);
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(vessel.LastUpdateUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return vessel;
        }

        /// <summary>
        /// Delete a vessel by identifier.
        /// </summary>
        /// <param name="id">Vessel identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM vessels WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Enumerate all vessels.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of all vessels.</returns>
        public async Task<List<Vessel>> EnumerateAsync(CancellationToken token = default)
        {
            List<Vessel> results = new List<Vessel>();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM vessels ORDER BY name;";
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(VesselFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Enumerate vessels with pagination and filtering.
        /// </summary>
        /// <param name="query">Enumeration query parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated enumeration result.</returns>
        public async Task<EnumerationResult<Vessel>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) query = new EnumerationQuery();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<MySqlParameter> parameters = new List<MySqlParameter>();

                if (query.CreatedAfter.HasValue)
                {
                    conditions.Add("created_utc > @created_after");
                    parameters.Add(new MySqlParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                }
                if (query.CreatedBefore.HasValue)
                {
                    conditions.Add("created_utc < @created_before");
                    parameters.Add(new MySqlParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                }
                if (!string.IsNullOrEmpty(query.FleetId))
                {
                    conditions.Add("fleet_id = @fleet_id");
                    parameters.Add(new MySqlParameter("@fleet_id", query.FleetId));
                }

                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                // Count
                long totalCount = 0;
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM vessels" + whereClause + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                // Query
                List<Vessel> results = new List<Vessel>();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM vessels" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(VesselFromReader(reader));
                    }
                }

                return EnumerationResult<Vessel>.Create(query, results, totalCount);
            }
        }

        /// <summary>
        /// Enumerate vessels by fleet identifier.
        /// </summary>
        /// <param name="fleetId">Fleet identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of vessels in the fleet.</returns>
        public async Task<List<Vessel>> EnumerateByFleetAsync(string fleetId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(fleetId)) throw new ArgumentNullException(nameof(fleetId));
            List<Vessel> results = new List<Vessel>();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM vessels WHERE fleet_id = @fleet_id ORDER BY name;";
                    cmd.Parameters.AddWithValue("@fleet_id", fleetId);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(VesselFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Check if a vessel exists by identifier.
        /// </summary>
        /// <param name="id">Vessel identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the vessel exists.</returns>
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM vessels WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                    return count > 0;
                }
            }
        }

        /// <summary>
        /// Read a vessel by tenant and identifier (tenant-scoped).
        /// </summary>
        public async Task<Vessel?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM vessels WHERE tenant_id = @tenantId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return VesselFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Delete a vessel by tenant and identifier (tenant-scoped).
        /// </summary>
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM vessels WHERE tenant_id = @tenantId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Enumerate all vessels in a tenant (tenant-scoped).
        /// </summary>
        public async Task<List<Vessel>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            List<Vessel> results = new List<Vessel>();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM vessels WHERE tenant_id = @tenantId ORDER BY name;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(VesselFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Enumerate vessels with pagination and filtering (tenant-scoped).
        /// </summary>
        public async Task<EnumerationResult<Vessel>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) query = new EnumerationQuery();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string> { "tenant_id = @tenantId" };
                List<MySqlParameter> parameters = new List<MySqlParameter> { new MySqlParameter("@tenantId", tenantId) };

                if (query.CreatedAfter.HasValue)
                {
                    conditions.Add("created_utc > @created_after");
                    parameters.Add(new MySqlParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                }
                if (query.CreatedBefore.HasValue)
                {
                    conditions.Add("created_utc < @created_before");
                    parameters.Add(new MySqlParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                }
                if (!string.IsNullOrEmpty(query.FleetId))
                {
                    conditions.Add("fleet_id = @fleet_id");
                    parameters.Add(new MySqlParameter("@fleet_id", query.FleetId));
                }

                string whereClause = " WHERE " + string.Join(" AND ", conditions);
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                long totalCount = 0;
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM vessels" + whereClause + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                List<Vessel> results = new List<Vessel>();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM vessels" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(VesselFromReader(reader));
                    }
                }

                return EnumerationResult<Vessel>.Create(query, results, totalCount);
            }
        }

        /// <inheritdoc />
        public async Task<Vessel?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM vessels WHERE tenant_id = @tenantId AND name = @name;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@name", name);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return VesselFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<List<Vessel>> EnumerateByFleetAsync(string tenantId, string fleetId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(fleetId)) throw new ArgumentNullException(nameof(fleetId));
            List<Vessel> results = new List<Vessel>();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM vessels WHERE tenant_id = @tenantId AND fleet_id = @fleet_id ORDER BY name;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@fleet_id", fleetId);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(VesselFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM vessels WHERE tenant_id = @tenantId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                    return count > 0;
                }
            }
        }

        /// <inheritdoc />
        public async Task<Vessel?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM vessels WHERE tenant_id = @tenantId AND user_id = @userId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@userId", userId);
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return VesselFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM vessels WHERE tenant_id = @tenantId AND user_id = @userId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@userId", userId);
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<List<Vessel>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            List<Vessel> results = new List<Vessel>();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM vessels WHERE tenant_id = @tenantId AND user_id = @userId ORDER BY name;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@userId", userId);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(VesselFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Vessel>> EnumerateAsync(string tenantId, string userId, EnumerationQuery query, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (query == null) query = new EnumerationQuery();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string> { "tenant_id = @tenantId", "user_id = @userId" };
                List<MySqlParameter> parameters = new List<MySqlParameter>
                {
                    new MySqlParameter("@tenantId", tenantId),
                    new MySqlParameter("@userId", userId)
                };

                if (query.CreatedAfter.HasValue)
                {
                    conditions.Add("created_utc > @created_after");
                    parameters.Add(new MySqlParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                }
                if (query.CreatedBefore.HasValue)
                {
                    conditions.Add("created_utc < @created_before");
                    parameters.Add(new MySqlParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                }
                if (!string.IsNullOrEmpty(query.FleetId))
                {
                    conditions.Add("fleet_id = @fleet_id");
                    parameters.Add(new MySqlParameter("@fleet_id", query.FleetId));
                }

                string whereClause = " WHERE " + string.Join(" AND ", conditions);
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                long totalCount = 0;
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM vessels" + whereClause + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                List<Vessel> results = new List<Vessel>();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM vessels" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(VesselFromReader(reader));
                    }
                }

                return EnumerationResult<Vessel>.Create(query, results, totalCount);
            }
        }

        #endregion

        #region Private-Methods

        private static string ToIso8601(DateTime dt)
        {
            return dt.ToUniversalTime().ToString(_Iso8601Format, CultureInfo.InvariantCulture);
        }

        private static DateTime FromIso8601(string value)
        {
            return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        private static string? NullableString(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            return string.IsNullOrEmpty(str) ? null : str;
        }

        private static Vessel VesselFromReader(MySqlDataReader reader)
        {
            Vessel vessel = new Vessel();
            vessel.Id = reader["id"].ToString()!;
            vessel.TenantId = NullableString(reader["tenant_id"]);
            vessel.UserId = NullableString(reader["user_id"]);
            vessel.FleetId = NullableString(reader["fleet_id"]);
            vessel.Name = reader["name"].ToString()!;
            vessel.RepoUrl = NullableString(reader["repo_url"]);
            vessel.LocalPath = NullableString(reader["local_path"]);
            vessel.WorkingDirectory = NullableString(reader["working_directory"]);
            try { vessel.PreferredHarborId = NullableString(reader["preferred_harbor_id"]); } catch { }
            try { vessel.RequiredCapabilities = NullableString(reader["required_capabilities"]); } catch { }
            vessel.ProjectContext = NullableString(reader["project_context"]);
            vessel.StyleGuide = NullableString(reader["style_guide"]);
            try { vessel.EnableModelContext = Convert.ToInt64(reader["enable_model_context"]) == 1; }
            catch { vessel.EnableModelContext = true; }
            vessel.ModelContext = NullableString(reader["model_context"]);
            try
            {
                vessel.GitHubTokenOverride = NullableString(reader["github_token_override"]);
                vessel.NormalizeGitHubTokenOverride();
            }
            catch { }
            string? landingModeStr = NullableString(reader["landing_mode"]);
            if (!String.IsNullOrEmpty(landingModeStr) && Enum.TryParse<LandingModeEnum>(landingModeStr, out LandingModeEnum lm))
                vessel.LandingMode = lm;
            string? branchCleanupStr = NullableString(reader["branch_cleanup_policy"]);
            if (!String.IsNullOrEmpty(branchCleanupStr) && Enum.TryParse<BranchCleanupPolicyEnum>(branchCleanupStr, out BranchCleanupPolicyEnum bcp))
                vessel.BranchCleanupPolicy = bcp;
            try { vessel.AllowConcurrentMissions = Convert.ToInt64(reader["allow_concurrent_missions"]) == 1; }
            catch { vessel.AllowConcurrentMissions = false; }
            try { vessel.SecretScanEnabled = Convert.ToInt64(reader["secret_scan_enabled"]) == 1; } catch { }
            try
            {
                string? protectedPathJson = NullableString(reader["protected_path_patterns_json"]);
                if (!String.IsNullOrWhiteSpace(protectedPathJson))
                    vessel.ProtectedPathPatterns = JsonSerializer.Deserialize<List<string>>(protectedPathJson) ?? new List<string>();
            }
            catch { }
            try
            {
                string? denylistJson = NullableString(reader["private_identifier_denylist_json"]);
                if (!String.IsNullOrWhiteSpace(denylistJson))
                    vessel.PrivateIdentifierDenylist = JsonSerializer.Deserialize<List<string>>(denylistJson) ?? new List<string>();
            }
            catch { }
            try { vessel.AutoLandEnabled = Convert.ToInt64(reader["auto_land_enabled"]) == 1; } catch { }
            try { vessel.AutoLandMaxFiles = Convert.ToInt32(reader["auto_land_max_files"]); } catch { }
            try { vessel.AutoLandMaxLines = Convert.ToInt32(reader["auto_land_max_lines"]); } catch { }
            try
            {
                string? allowGlobsJson = NullableString(reader["auto_land_path_allow_globs_json"]);
                if (!String.IsNullOrWhiteSpace(allowGlobsJson))
                    vessel.AutoLandPathAllowGlobs = JsonSerializer.Deserialize<List<string>>(allowGlobsJson) ?? new List<string>();
            }
            catch { }
            try
            {
                string? denyGlobsJson = NullableString(reader["auto_land_path_deny_globs_json"]);
                if (!String.IsNullOrWhiteSpace(denyGlobsJson))
                    vessel.AutoLandPathDenyGlobs = JsonSerializer.Deserialize<List<string>>(denyGlobsJson) ?? new List<string>();
            }
            catch { }
            try { vessel.DefinitionOfDoneEnabled = Convert.ToInt64(reader["definition_of_done_enabled"]) == 1; } catch { }
            try { vessel.DefinitionOfDoneBuildCommand = NullableString(reader["definition_of_done_build_command"]); } catch { }
            try { vessel.DefinitionOfDoneTestCommand = NullableString(reader["definition_of_done_test_command"]); } catch { }
            try { vessel.DefinitionOfDoneTimeoutSeconds = Convert.ToInt32(reader["definition_of_done_timeout_seconds"]); } catch { }
            try { vessel.DefaultPipelineId = NullableString(reader["default_pipeline_id"]); } catch { }
            try { vessel.RequirePassingChecksToLand = Convert.ToInt64(reader["require_passing_checks_to_land"]) == 1; } catch { }
            vessel.DefaultBranch = reader["default_branch"].ToString()!;
            vessel.Active = Convert.ToInt64(reader["active"]) == 1;
            vessel.CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc);
            vessel.LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc);
            return vessel;
        }

        #endregion
    }
}

