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
    /// MySQL implementation of managed model endpoint persistence.
    /// </summary>
    public class ModelEndpointMethods : IModelEndpointMethods
    {
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions();

        private readonly string _ConnectionString;

        /// <summary>
        /// Initializes a new instance of the <see cref="ModelEndpointMethods"/> class.
        /// </summary>
        public ModelEndpointMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        /// <inheritdoc />
        public async Task<ModelEndpoint> CreateAsync(ModelEndpoint endpoint, CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO model_endpoints
                        (id, tenant_id, user_id, name, kind, provider, base_url, api_key, model, dimensionality, timeout_ms, enabled, health_status, last_health_check_utc, last_health_error, last_latency_ms, health_history_json, created_utc, last_update_utc)
                        VALUES
                        (@id, @tenant_id, @user_id, @name, @kind, @provider, @base_url, @api_key, @model, @dimensionality, @timeout_ms, @enabled, @health_status, @last_health_check_utc, @last_health_error, @last_latency_ms, @health_history_json, @created_utc, @last_update_utc);";
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
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE model_endpoints SET
                        tenant_id = @tenant_id,
                        user_id = @user_id,
                        name = @name,
                        kind = @kind,
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
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
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
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM model_endpoints WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false) ?? 0L);
                    return count > 0;
                }
            }
        }

        private async Task<ModelEndpoint?> ReadInternalAsync(string sql, Action<MySqlCommand> parameterize, CancellationToken token)
        {
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize(cmd);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return EndpointFromReader(reader);
                    }
                }
            }

            return null;
        }

        private async Task<List<ModelEndpoint>> EnumerateInternalAsync(string sql, Action<MySqlCommand>? parameterize, CancellationToken token)
        {
            List<ModelEndpoint> results = new List<ModelEndpoint>();
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize?.Invoke(cmd);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(EndpointFromReader(reader));
                    }
                }
            }

            return results;
        }

        private async Task ExecuteDeleteAsync(string sql, Action<MySqlCommand> parameterize, CancellationToken token)
        {
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize(cmd);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        private static void BindEndpoint(MySqlCommand cmd, ModelEndpoint endpoint)
        {
            cmd.Parameters.AddWithValue("@id", endpoint.Id);
            cmd.Parameters.AddWithValue("@tenant_id", (object?)endpoint.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_id", (object?)endpoint.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@name", endpoint.Name);
            cmd.Parameters.AddWithValue("@kind", endpoint.Kind.ToString());
            cmd.Parameters.AddWithValue("@provider", endpoint.Provider.ToString());
            cmd.Parameters.AddWithValue("@base_url", endpoint.BaseUrl);
            cmd.Parameters.AddWithValue("@api_key", (object?)endpoint.ApiKey ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@model", (object?)endpoint.Model ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dimensionality", endpoint.Dimensionality);
            cmd.Parameters.AddWithValue("@timeout_ms", endpoint.TimeoutMs);
            cmd.Parameters.AddWithValue("@enabled", endpoint.Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@health_status", endpoint.HealthStatus.ToString());
            cmd.Parameters.AddWithValue("@last_health_check_utc", endpoint.LastHealthCheckUtc.HasValue ? (object)MysqlDatabaseDriver.ToIso8601(endpoint.LastHealthCheckUtc.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@last_health_error", (object?)endpoint.LastHealthError ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@last_latency_ms", endpoint.LastLatencyMs.HasValue ? (object)endpoint.LastLatencyMs.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@health_history_json", JsonSerializer.Serialize(endpoint.HealthHistory ?? new List<ModelEndpointHealthRecord>(), _Json));
            cmd.Parameters.AddWithValue("@created_utc", MysqlDatabaseDriver.ToIso8601(endpoint.CreatedUtc));
            cmd.Parameters.AddWithValue("@last_update_utc", MysqlDatabaseDriver.ToIso8601(endpoint.LastUpdateUtc));
        }

        private static ModelEndpoint EndpointFromReader(MySqlDataReader reader)
        {
            ModelEndpoint endpoint = new ModelEndpoint
            {
                Id = reader["id"].ToString()!,
                TenantId = MysqlDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = MysqlDatabaseDriver.NullableString(reader["user_id"]),
                Name = reader["name"].ToString()!,
                Kind = ParseEnum(reader["kind"], ModelEndpointKindEnum.Inference),
                Provider = ParseEnum(reader["provider"], ModelProviderEnum.OpenAI),
                BaseUrl = reader["base_url"].ToString()!,
                Model = MysqlDatabaseDriver.NullableString(reader["model"]),
                Dimensionality = MysqlDatabaseDriver.NullableInt(reader["dimensionality"]) ?? 0,
                TimeoutMs = MysqlDatabaseDriver.NullableInt(reader["timeout_ms"]) ?? 120000,
                Enabled = (MysqlDatabaseDriver.NullableInt(reader["enabled"]) ?? 1) != 0,
                HealthStatus = ParseEnum(reader["health_status"], EndpointHealthStatusEnum.Unknown),
                LastHealthCheckUtc = MysqlDatabaseDriver.FromIso8601Nullable(reader["last_health_check_utc"]),
                LastHealthError = MysqlDatabaseDriver.NullableString(reader["last_health_error"]),
                LastLatencyMs = NullableLong(reader["last_latency_ms"]),
                CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc),
                LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc)
            };

            endpoint.ApiKey = MysqlDatabaseDriver.NullableString(reader["api_key"]);

            string? historyJson = MysqlDatabaseDriver.NullableString(reader["health_history_json"]);
            if (!String.IsNullOrWhiteSpace(historyJson))
                endpoint.HealthHistory = JsonSerializer.Deserialize<List<ModelEndpointHealthRecord>>(historyJson, _Json) ?? new List<ModelEndpointHealthRecord>();

            return endpoint;
        }

        private static long? NullableLong(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            return Convert.ToInt64(value);
        }

        private static TEnum ParseEnum<TEnum>(object value, TEnum fallback) where TEnum : struct
        {
            if (value == null || value == DBNull.Value) return fallback;
            return Enum.TryParse<TEnum>(value.ToString(), true, out TEnum parsed) ? parsed : fallback;
        }
    }
}
