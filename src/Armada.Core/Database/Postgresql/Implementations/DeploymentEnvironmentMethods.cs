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
    /// PostgreSQL implementation of deployment-environment persistence.
    /// </summary>
    public class DeploymentEnvironmentMethods : IDeploymentEnvironmentMethods
    {
        private readonly PostgresqlDatabaseDriver _Driver;
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DeploymentEnvironmentMethods(PostgresqlDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc />
        public async Task<DeploymentEnvironment> CreateAsync(DeploymentEnvironment environment, CancellationToken token = default)
        {
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            environment.LastUpdateUtc = DateTime.UtcNow;

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO environments
                        (id, tenant_id, user_id, vessel_id, name, description, kind, configuration_source, base_url, health_endpoint,
                         access_notes, deployment_rules, verification_definitions_json, rollout_monitoring_window_minutes, rollout_monitoring_interval_seconds,
                         alert_on_regression, requires_approval, is_default, active, created_utc, last_update_utc)
                        VALUES
                        (@id, @tenant_id, @user_id, @vessel_id, @name, @description, @kind, @configuration_source, @base_url, @health_endpoint,
                         @access_notes, @deployment_rules, @verification_definitions_json, @rollout_monitoring_window_minutes, @rollout_monitoring_interval_seconds,
                         @alert_on_regression, @requires_approval, @is_default, @active, @created_utc, @last_update_utc);";
                    AddParameters(cmd, environment);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return environment;
        }

        /// <inheritdoc />
        public async Task<DeploymentEnvironment?> ReadAsync(string id, DeploymentEnvironmentQuery? query = null, CancellationToken token = default)
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
                    cmd.CommandText = "SELECT * FROM environments WHERE " + String.Join(" AND ", conditions) + " LIMIT 1;";
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
        public async Task<DeploymentEnvironment> UpdateAsync(DeploymentEnvironment environment, CancellationToken token = default)
        {
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            environment.LastUpdateUtc = DateTime.UtcNow;

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE environments SET
                        tenant_id = @tenant_id,
                        user_id = @user_id,
                        vessel_id = @vessel_id,
                        name = @name,
                        description = @description,
                        kind = @kind,
                        configuration_source = @configuration_source,
                        base_url = @base_url,
                        health_endpoint = @health_endpoint,
                        access_notes = @access_notes,
                        deployment_rules = @deployment_rules,
                        verification_definitions_json = @verification_definitions_json,
                        rollout_monitoring_window_minutes = @rollout_monitoring_window_minutes,
                        rollout_monitoring_interval_seconds = @rollout_monitoring_interval_seconds,
                        alert_on_regression = @alert_on_regression,
                        requires_approval = @requires_approval,
                        is_default = @is_default,
                        active = @active,
                        last_update_utc = @last_update_utc
                        WHERE id = @id;";
                    AddParameters(cmd, environment);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return environment;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, DeploymentEnvironmentQuery? query = null, CancellationToken token = default)
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
                    cmd.CommandText = "DELETE FROM environments WHERE " + String.Join(" AND ", conditions) + ";";
                    foreach (NpgsqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<DeploymentEnvironment>> EnumerateAsync(DeploymentEnvironmentQuery query, CancellationToken token = default)
        {
            query ??= new DeploymentEnvironmentQuery();
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
                    countCmd.CommandText = "SELECT COUNT(*) FROM environments" + whereClause + ";";
                    foreach (NpgsqlParameter parameter in parameters) countCmd.Parameters.Add(CloneParameter(parameter));
                    totalCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                List<DeploymentEnvironment> results = new List<DeploymentEnvironment>();
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM environments" + whereClause
                        + " ORDER BY is_default DESC, active DESC, name ASC, created_utc DESC, id DESC LIMIT @page_size OFFSET @offset;";
                    foreach (NpgsqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    cmd.Parameters.AddWithValue("@page_size", pageSize);
                    cmd.Parameters.AddWithValue("@offset", offset);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(FromReader(reader));
                    }
                }

                return new EnumerationResult<DeploymentEnvironment>
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
        public async Task<List<DeploymentEnvironment>> EnumerateAllAsync(DeploymentEnvironmentQuery query, CancellationToken token = default)
        {
            query ??= new DeploymentEnvironmentQuery();

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<NpgsqlParameter> parameters = new List<NpgsqlParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

                List<DeploymentEnvironment> results = new List<DeploymentEnvironment>();
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM environments" + whereClause + " ORDER BY is_default DESC, active DESC, name ASC, created_utc DESC, id DESC;";
                    foreach (NpgsqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(FromReader(reader));
                    }
                }

                return results;
            }
        }

        private static void ApplyQueryFilters(DeploymentEnvironmentQuery? query, List<string> conditions, List<NpgsqlParameter> parameters)
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
            if (!String.IsNullOrWhiteSpace(query.VesselId))
            {
                conditions.Add("vessel_id = @vessel_id");
                parameters.Add(new NpgsqlParameter("@vessel_id", query.VesselId));
            }
            if (query.Kind.HasValue)
            {
                conditions.Add("kind = @kind");
                parameters.Add(new NpgsqlParameter("@kind", query.Kind.Value.ToString()));
            }
            if (query.IsDefault.HasValue)
            {
                conditions.Add("is_default = @is_default_filter");
                parameters.Add(new NpgsqlParameter("@is_default_filter", query.IsDefault.Value));
            }
            if (query.Active.HasValue)
            {
                conditions.Add("active = @active_filter");
                parameters.Add(new NpgsqlParameter("@active_filter", query.Active.Value));
            }
            if (!String.IsNullOrWhiteSpace(query.Search))
            {
                conditions.Add("(LOWER(name) LIKE @search OR LOWER(COALESCE(description, '')) LIKE @search OR LOWER(COALESCE(configuration_source, '')) LIKE @search OR LOWER(COALESCE(base_url, '')) LIKE @search OR LOWER(COALESCE(health_endpoint, '')) LIKE @search)");
                parameters.Add(new NpgsqlParameter("@search", "%" + query.Search.ToLowerInvariant() + "%"));
            }
        }

        private static void AddParameters(NpgsqlCommand cmd, DeploymentEnvironment environment)
        {
            cmd.Parameters.AddWithValue("@id", environment.Id);
            cmd.Parameters.AddWithValue("@tenant_id", (object?)environment.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_id", (object?)environment.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vessel_id", (object?)environment.VesselId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@name", environment.Name);
            cmd.Parameters.AddWithValue("@description", (object?)environment.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@kind", environment.Kind.ToString());
            cmd.Parameters.AddWithValue("@configuration_source", (object?)environment.ConfigurationSource ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@base_url", (object?)environment.BaseUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@health_endpoint", (object?)environment.HealthEndpoint ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@access_notes", (object?)environment.AccessNotes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@deployment_rules", (object?)environment.DeploymentRules ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@verification_definitions_json", JsonSerializer.Serialize(environment.VerificationDefinitions ?? new List<DeploymentVerificationDefinition>(), _Json));
            cmd.Parameters.AddWithValue("@rollout_monitoring_window_minutes", environment.RolloutMonitoringWindowMinutes);
            cmd.Parameters.AddWithValue("@rollout_monitoring_interval_seconds", environment.RolloutMonitoringIntervalSeconds);
            cmd.Parameters.AddWithValue("@alert_on_regression", environment.AlertOnRegression);
            cmd.Parameters.AddWithValue("@requires_approval", environment.RequiresApproval);
            cmd.Parameters.AddWithValue("@is_default", environment.IsDefault);
            cmd.Parameters.AddWithValue("@active", environment.Active);
            PostgresqlCommandHelper.AddDate(cmd, "@created_utc", environment.CreatedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", environment.LastUpdateUtc);
        }

        private static DeploymentEnvironment FromReader(NpgsqlDataReader reader)
        {
            DeploymentEnvironment environment = new DeploymentEnvironment
            {
                Id = reader["id"].ToString() ?? String.Empty,
                TenantId = NullableString(reader["tenant_id"]),
                UserId = NullableString(reader["user_id"]),
                VesselId = NullableString(reader["vessel_id"]),
                Name = reader["name"].ToString() ?? String.Empty,
                Description = NullableString(reader["description"]),
                ConfigurationSource = NullableString(reader["configuration_source"]),
                BaseUrl = NullableString(reader["base_url"]),
                HealthEndpoint = NullableString(reader["health_endpoint"]),
                AccessNotes = NullableString(reader["access_notes"]),
                DeploymentRules = NullableString(reader["deployment_rules"]),
                RolloutMonitoringWindowMinutes = reader["rollout_monitoring_window_minutes"] == DBNull.Value ? 0 : Convert.ToInt32(reader["rollout_monitoring_window_minutes"]),
                RolloutMonitoringIntervalSeconds = reader["rollout_monitoring_interval_seconds"] == DBNull.Value ? 300 : Convert.ToInt32(reader["rollout_monitoring_interval_seconds"]),
                AlertOnRegression = reader["alert_on_regression"] == DBNull.Value || Convert.ToBoolean(reader["alert_on_regression"]),
                RequiresApproval = Convert.ToBoolean(reader["requires_approval"]),
                IsDefault = Convert.ToBoolean(reader["is_default"]),
                Active = Convert.ToBoolean(reader["active"]),
                CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc),
                LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc)
            };

            if (Enum.TryParse(reader["kind"].ToString(), true, out EnvironmentKindEnum kind))
                environment.Kind = kind;
            environment.VerificationDefinitions = Deserialize<List<DeploymentVerificationDefinition>>(NullableString(reader["verification_definitions_json"])) ?? new List<DeploymentVerificationDefinition>();

            return environment;
        }

        private static string? NullableString(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string stringValue = value.ToString()!;
            return String.IsNullOrEmpty(stringValue) ? null : stringValue;
        }

        private static NpgsqlParameter CloneParameter(NpgsqlParameter parameter)
        {
            return new NpgsqlParameter(parameter.ParameterName, parameter.Value ?? DBNull.Value);
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
