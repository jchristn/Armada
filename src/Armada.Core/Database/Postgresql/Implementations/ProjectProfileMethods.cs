namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// PostgreSQL implementation of project-profile persistence.
    /// </summary>
    public class ProjectProfileMethods : IProjectProfileMethods
    {
        private readonly PostgresqlDatabaseDriver _Driver;
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ProjectProfileMethods(PostgresqlDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc />
        public async Task<ProjectProfile> CreateAsync(ProjectProfile profile, CancellationToken token = default)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            profile.LastUpdateUtc = DateTime.UtcNow;

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO project_profiles
                        (id, tenant_id, user_id, name, description, scope, ownership_scope, fleet_id, vessel_id, is_default, active,
                         default_pipeline_id, workflow_profile_id, persona_overrides_json, skills_json,
                         created_utc, last_update_utc)
                        VALUES
                        (@id, @tenant_id, @user_id, @name, @description, @scope, @ownership_scope, @fleet_id, @vessel_id, @is_default, @active,
                         @default_pipeline_id, @workflow_profile_id, @persona_overrides_json, @skills_json,
                         @created_utc, @last_update_utc);";
                    AddParameters(cmd, profile);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return profile;
        }

        /// <inheritdoc />
        public async Task<ProjectProfile?> ReadAsync(string id, ProjectProfileQuery? query = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string> { "id = @id" };
                    List<NpgsqlParameter> parameters = new List<NpgsqlParameter> { new NpgsqlParameter("@id", id) };
                    ApplyQueryFilters(query, conditions, parameters);
                    cmd.CommandText = "SELECT * FROM project_profiles WHERE " + String.Join(" AND ", conditions) + " LIMIT 1;";
                    foreach (NpgsqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return FromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<ProjectProfile> UpdateAsync(ProjectProfile profile, CancellationToken token = default)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            profile.LastUpdateUtc = DateTime.UtcNow;

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE project_profiles SET
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
                        default_pipeline_id = @default_pipeline_id,
                        workflow_profile_id = @workflow_profile_id,
                        persona_overrides_json = @persona_overrides_json,
                        skills_json = @skills_json,
                        last_update_utc = @last_update_utc
                        WHERE id = @id;";
                    AddParameters(cmd, profile);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return profile;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, ProjectProfileQuery? query = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string> { "id = @id" };
                    List<NpgsqlParameter> parameters = new List<NpgsqlParameter> { new NpgsqlParameter("@id", id) };
                    ApplyQueryFilters(query, conditions, parameters);
                    cmd.CommandText = "DELETE FROM project_profiles WHERE " + String.Join(" AND ", conditions) + ";";
                    foreach (NpgsqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<ProjectProfile>> EnumerateAsync(ProjectProfileQuery query, CancellationToken token = default)
        {
            query ??= new ProjectProfileQuery();
            int pageSize = query.PageSize <= 0 ? 25 : query.PageSize;
            int offset = query.PageNumber <= 1 ? 0 : (query.PageNumber - 1) * pageSize;

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<NpgsqlParameter> parameters = new List<NpgsqlParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

                long totalCount;
                using (NpgsqlCommand countCmd = conn.CreateCommand())
                {
                    countCmd.CommandText = "SELECT COUNT(*) FROM project_profiles" + whereClause + ";";
                    foreach (NpgsqlParameter parameter in parameters) countCmd.Parameters.Add(CloneParameter(parameter));
                    totalCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                List<ProjectProfile> results = new List<ProjectProfile>();
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM project_profiles" + whereClause
                        + " ORDER BY is_default DESC, last_update_utc DESC, name ASC LIMIT @page_size OFFSET @offset;";
                    foreach (NpgsqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    cmd.Parameters.AddWithValue("@page_size", pageSize);
                    cmd.Parameters.AddWithValue("@offset", offset);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(FromReader(reader));
                    }
                }

                return new EnumerationResult<ProjectProfile>
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
        public async Task<List<ProjectProfile>> EnumerateAllAsync(ProjectProfileQuery query, CancellationToken token = default)
        {
            query ??= new ProjectProfileQuery();

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string>();
                    List<NpgsqlParameter> parameters = new List<NpgsqlParameter>();
                    ApplyQueryFilters(query, conditions, parameters);
                    string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;
                    cmd.CommandText = "SELECT * FROM project_profiles" + whereClause + " ORDER BY is_default DESC, last_update_utc DESC, name ASC;";
                    foreach (NpgsqlParameter parameter in parameters) cmd.Parameters.Add(parameter);

                    List<ProjectProfile> results = new List<ProjectProfile>();
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(FromReader(reader));
                    }

                    return results;
                }
            }
        }

        private static void ApplyQueryFilters(ProjectProfileQuery? query, List<string> conditions, List<NpgsqlParameter> parameters)
        {
            if (query == null) return;

            if (!String.IsNullOrWhiteSpace(query.TenantId))
            {
                conditions.Add("tenant_id = @tenant_id");
                parameters.Add(new NpgsqlParameter("@tenant_id", query.TenantId));
            }
            if (!String.IsNullOrWhiteSpace(query.UserId))
            {
                conditions.Add("user_id = @user_id");
                parameters.Add(new NpgsqlParameter("@user_id", query.UserId));
            }
            if (query.Scope.HasValue)
            {
                conditions.Add("scope = @scope");
                parameters.Add(new NpgsqlParameter("@scope", query.Scope.Value.ToString()));
            }
            if (!String.IsNullOrWhiteSpace(query.FleetId))
            {
                conditions.Add("fleet_id = @fleet_id");
                parameters.Add(new NpgsqlParameter("@fleet_id", query.FleetId));
            }
            if (!String.IsNullOrWhiteSpace(query.VesselId))
            {
                conditions.Add("vessel_id = @vessel_id");
                parameters.Add(new NpgsqlParameter("@vessel_id", query.VesselId));
            }
            if (!String.IsNullOrWhiteSpace(query.Search))
            {
                conditions.Add("(name ILIKE @search OR COALESCE(description, '') ILIKE @search)");
                parameters.Add(new NpgsqlParameter("@search", "%" + query.Search + "%"));
            }
            if (query.Active.HasValue)
            {
                conditions.Add("active = @active");
                parameters.Add(new NpgsqlParameter("@active", query.Active.Value));
            }
            if (query.FromUtc.HasValue)
            {
                conditions.Add("created_utc >= @from_utc");
                parameters.Add(new NpgsqlParameter("@from_utc", query.FromUtc.Value));
            }
            if (query.ToUtc.HasValue)
            {
                conditions.Add("created_utc <= @to_utc");
                parameters.Add(new NpgsqlParameter("@to_utc", query.ToUtc.Value));
            }
        }

        private static void AddParameters(NpgsqlCommand cmd, ProjectProfile profile)
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
            cmd.Parameters.AddWithValue("@default_pipeline_id", (object?)profile.DefaultPipelineId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@workflow_profile_id", (object?)profile.WorkflowProfileId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@persona_overrides_json", Serialize(profile.PersonaOverrides));
            cmd.Parameters.AddWithValue("@skills_json", Serialize(profile.Skills));
            cmd.Parameters.AddWithValue("@created_utc", profile.CreatedUtc);
            cmd.Parameters.AddWithValue("@last_update_utc", profile.LastUpdateUtc);
        }

        private static ProjectProfile FromReader(NpgsqlDataReader reader)
        {
            ProjectProfile profile = new ProjectProfile
            {
                Id = reader["id"].ToString() ?? String.Empty,
                TenantId = NullableString(reader["tenant_id"]),
                UserId = NullableString(reader["user_id"]),
                Name = reader["name"].ToString() ?? String.Empty,
                Description = NullableString(reader["description"]),
                FleetId = NullableString(reader["fleet_id"]),
                VesselId = NullableString(reader["vessel_id"]),
                IsDefault = Convert.ToBoolean(reader["is_default"]),
                Active = Convert.ToBoolean(reader["active"]),
                DefaultPipelineId = NullableString(reader["default_pipeline_id"]),
                WorkflowProfileId = NullableString(reader["workflow_profile_id"]),
                CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc),
                LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc)
            };

            if (Enum.TryParse(reader["scope"].ToString(), true, out ProjectProfileScopeEnum scope))
                profile.Scope = scope;
                profile.OwnershipScope = System.Enum.TryParse<Armada.Core.Enums.ScopeEnum>(reader["ownership_scope"]?.ToString(), true, out Armada.Core.Enums.ScopeEnum __os) ? __os : Armada.Core.Enums.ScopeEnum.TenantWide;

            profile.PersonaOverrides = Deserialize<List<PersonaOverride>>(NullableString(reader["persona_overrides_json"])) ?? new List<PersonaOverride>();
            profile.Skills = Deserialize<List<string>>(NullableString(reader["skills_json"])) ?? new List<string>();
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

        private static string? NullableString(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            return String.IsNullOrEmpty(str) ? null : str;
        }

        private static NpgsqlParameter CloneParameter(NpgsqlParameter parameter)
        {
            return new NpgsqlParameter(parameter.ParameterName, parameter.Value ?? DBNull.Value);
        }
    }
}
