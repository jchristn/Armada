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
    /// PostgreSQL implementation of managed model endpoint persistence.
    /// </summary>
    public class ModelEndpointMethods : IModelEndpointMethods
    {
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions();

        private readonly PostgresqlDatabaseDriver _Driver;

        /// <summary>
        /// Initializes a new instance of the <see cref="ModelEndpointMethods"/> class.
        /// </summary>
        public ModelEndpointMethods(PostgresqlDatabaseDriver driver, Settings.DatabaseSettings settings, SyslogLogging.LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc />
        public async Task<ModelEndpoint> CreateAsync(ModelEndpoint endpoint, CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO model_endpoints
                        (id, tenant_id, user_id, name, kind, scope, provider, base_url, api_key, model, dimensionality, timeout_ms, enabled, health_status, last_health_check_utc, last_health_error, last_latency_ms, health_history_json, created_utc, last_update_utc)
                        VALUES
                        (@id, @tenant_id, @user_id, @name, @kind, @scope, @provider, @base_url, @api_key, @model, @dimensionality, @timeout_ms, @enabled, @health_status, @last_health_check_utc, @last_health_error, @last_latency_ms, @health_history_json, @created_utc, @last_update_utc);";
                    BindEndpoint(cmd, endpoint);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return endpoint;
        }

        /// <inheritdoc />
        public async Task<ModelEndpoint> UpdateAsync(ModelEndpoint endpoint, CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE model_endpoints SET
                        tenant_id = @tenant_id,
                        user_id = @user_id,
                        name = @name,
                        kind = @kind,
                        scope = @scope,
                        provider = @provider,
                        base_url = @base_url,
                        api_key = @api_key,
                        model = @model,
                        dimensionality = @dimensionality,
                        timeout_ms = @timeout_ms,
                        enabled = @enabled,
                        health_status = @health_status,
                        last_health_check_utc = @last_health_check_utc,
                        last_health_error = @last_health_error,
                        last_latency_ms = @last_latency_ms,
                        health_history_json = @health_history_json,
                        created_utc = @created_utc,
                        last_update_utc = @last_update_utc
                        WHERE id = @id;";
                    BindEndpoint(cmd, endpoint);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return endpoint;
        }

        /// <inheritdoc />
        public async Task<ModelEndpoint?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync(
                "SELECT * FROM model_endpoints WHERE id = @id;",
                cmd => cmd.Parameters.AddWithValue("@id", id),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ModelEndpoint?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync(
                "SELECT * FROM model_endpoints WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ModelEndpoint?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync(
                "SELECT * FROM model_endpoints WHERE tenant_id = @tenant_id AND user_id = @user_id AND id = @id;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@user_id", userId);
                    cmd.Parameters.AddWithValue("@id", id);
                },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            await ExecuteDeleteAsync(
                "DELETE FROM model_endpoints WHERE id = @id;",
                cmd => cmd.Parameters.AddWithValue("@id", id),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            await ExecuteDeleteAsync(
                "DELETE FROM model_endpoints WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ModelEndpoint>> EnumerateAsync(CancellationToken token = default)
        {
            return await EnumerateInternalAsync(
                "SELECT * FROM model_endpoints ORDER BY created_utc DESC;",
                null,
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ModelEndpoint>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return await EnumerateInternalAsync(
                "SELECT * FROM model_endpoints WHERE tenant_id = @tenant_id ORDER BY created_utc DESC;",
                cmd => cmd.Parameters.AddWithValue("@tenant_id", tenantId),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ModelEndpoint>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
            return await EnumerateInternalAsync(
                "SELECT * FROM model_endpoints WHERE tenant_id = @tenant_id AND user_id = @user_id ORDER BY created_utc DESC;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@user_id", userId);
                },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAnyAsync(CancellationToken token = default)
        {
            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT 1 FROM model_endpoints LIMIT 1;";
                    object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                    return result != null && result != DBNull.Value;
                }
            }
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM model_endpoints WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false) ?? 0L);
                    return count > 0;
                }
            }
        }

        private async Task<ModelEndpoint?> ReadInternalAsync(string sql, Action<NpgsqlCommand> parameterize, CancellationToken token)
        {
            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize(cmd);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return EndpointFromReader(reader);
                    }
                }
            }

            return null;
        }

        private async Task<List<ModelEndpoint>> EnumerateInternalAsync(string sql, Action<NpgsqlCommand>? parameterize, CancellationToken token)
        {
            List<ModelEndpoint> results = new List<ModelEndpoint>();
            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize?.Invoke(cmd);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(EndpointFromReader(reader));
                    }
                }
            }

            return results;
        }

        private async Task ExecuteDeleteAsync(string sql, Action<NpgsqlCommand> parameterize, CancellationToken token)
        {
            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize(cmd);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        private static void BindEndpoint(NpgsqlCommand cmd, ModelEndpoint endpoint)
        {
            cmd.Parameters.AddWithValue("@id", endpoint.Id);
            cmd.Parameters.AddWithValue("@tenant_id", (object?)endpoint.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_id", (object?)endpoint.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@name", endpoint.Name);
            cmd.Parameters.AddWithValue("@kind", endpoint.Kind.ToString());
            cmd.Parameters.AddWithValue("@scope", endpoint.Scope.ToString());
            cmd.Parameters.AddWithValue("@provider", endpoint.Provider.ToString());
            cmd.Parameters.AddWithValue("@base_url", endpoint.BaseUrl);
            cmd.Parameters.AddWithValue("@api_key", (object?)endpoint.ApiKey ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@model", (object?)endpoint.Model ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dimensionality", endpoint.Dimensionality);
            cmd.Parameters.AddWithValue("@timeout_ms", endpoint.TimeoutMs);
            cmd.Parameters.AddWithValue("@enabled", endpoint.Enabled);
            cmd.Parameters.AddWithValue("@health_status", endpoint.HealthStatus.ToString());
            cmd.Parameters.AddWithValue("@last_health_check_utc", (object?)endpoint.LastHealthCheckUtc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@last_health_error", (object?)endpoint.LastHealthError ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@last_latency_ms", (object?)endpoint.LastLatencyMs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@health_history_json", JsonSerializer.Serialize(endpoint.HealthHistory ?? new List<ModelEndpointHealthRecord>(), _Json));
            cmd.Parameters.AddWithValue("@created_utc", endpoint.CreatedUtc);
            cmd.Parameters.AddWithValue("@last_update_utc", endpoint.LastUpdateUtc);
        }

        private static ModelEndpoint EndpointFromReader(NpgsqlDataReader reader)
        {
            ModelEndpoint endpoint = new ModelEndpoint
            {
                Id = reader["id"].ToString()!,
                TenantId = NullableString(reader["tenant_id"]),
                UserId = NullableString(reader["user_id"]),
                Name = reader["name"].ToString()!,
                Kind = ParseEnum(reader["kind"], ModelEndpointKindEnum.Inference),
                Scope = ParseEnum(reader["scope"], ScopeEnum.TenantWide),
                Provider = ParseEnum(reader["provider"], ModelProviderEnum.OpenAI),
                BaseUrl = reader["base_url"].ToString()!,
                Model = NullableString(reader["model"]),
                Dimensionality = NullableInt(reader["dimensionality"]) ?? 0,
                TimeoutMs = NullableInt(reader["timeout_ms"]) ?? 120000,
                Enabled = NullableBool(reader["enabled"]) ?? true,
                HealthStatus = ParseEnum(reader["health_status"], EndpointHealthStatusEnum.Unknown),
                LastHealthCheckUtc = NullableDateTime(reader["last_health_check_utc"]),
                LastHealthError = NullableString(reader["last_health_error"]),
                LastLatencyMs = NullableLong(reader["last_latency_ms"]),
                CreatedUtc = (DateTime)reader["created_utc"],
                LastUpdateUtc = (DateTime)reader["last_update_utc"]
            };

            endpoint.ApiKey = NullableString(reader["api_key"]);

            string? historyJson = NullableString(reader["health_history_json"]);
            if (!String.IsNullOrWhiteSpace(historyJson))
                endpoint.HealthHistory = JsonSerializer.Deserialize<List<ModelEndpointHealthRecord>>(historyJson, _Json) ?? new List<ModelEndpointHealthRecord>();

            return endpoint;
        }

        private static TEnum ParseEnum<TEnum>(object value, TEnum fallback) where TEnum : struct
        {
            string? raw = value?.ToString();
            if (String.IsNullOrWhiteSpace(raw))
                return fallback;

            return Enum.TryParse<TEnum>(raw, true, out TEnum parsed) ? parsed : fallback;
        }

        private static string? NullableString(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            return String.IsNullOrEmpty(str) ? null : str;
        }

        private static DateTime? NullableDateTime(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            return DateTime.SpecifyKind(Convert.ToDateTime(value), DateTimeKind.Utc);
        }

        private static int? NullableInt(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            return Convert.ToInt32(value);
        }

        private static long? NullableLong(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            return Convert.ToInt64(value);
        }

        private static bool? NullableBool(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            return Convert.ToBoolean(value);
        }
    }
}
