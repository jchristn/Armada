namespace Armada.Core.Database.Sqlite.Implementations
{
    using System.Collections.Generic;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of Harbor (host runner) persistence, including the harbor_capabilities child
    /// table.
    /// </summary>
    public class HarborMethods : IHarborMethods
    {
        private readonly SqliteDatabaseDriver _Driver;
        private readonly DatabaseSettings _Settings;
        private readonly LoggingModule _Logging;

        /// <summary>
        /// Initializes a new instance of the <see cref="HarborMethods"/> class.
        /// </summary>
        public HarborMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <inheritdoc />
        public async Task<Harbor> CreateAsync(Harbor harbor, CancellationToken token = default)
        {
            if (harbor == null) throw new ArgumentNullException(nameof(harbor));
            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteTransaction tx = (SqliteTransaction)await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                {
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"INSERT INTO harbors
                            (id, tenant_id, user_id, name, connection_status, max_concurrent_jobs, enabled, protocol_version, os_platform, architecture, last_seen_utc, last_connected_utc, created_utc, last_update_utc)
                            VALUES
                            (@id, @tenant_id, @user_id, @name, @connection_status, @max_concurrent_jobs, @enabled, @protocol_version, @os_platform, @architecture, @last_seen_utc, @last_connected_utc, @created_utc, @last_update_utc);";
                        BindHarbor(cmd, harbor);
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    await ReplaceCapabilitiesAsync(conn, tx, harbor, token).ConfigureAwait(false);
                    await tx.CommitAsync(token).ConfigureAwait(false);
                }
            }

            return harbor;
        }

        /// <inheritdoc />
        public async Task<Harbor> UpdateAsync(Harbor harbor, CancellationToken token = default)
        {
            if (harbor == null) throw new ArgumentNullException(nameof(harbor));
            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteTransaction tx = (SqliteTransaction)await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                {
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"UPDATE harbors SET
                            tenant_id = @tenant_id,
                            user_id = @user_id,
                            name = @name,
                            connection_status = @connection_status,
                            max_concurrent_jobs = @max_concurrent_jobs,
                            enabled = @enabled,
                            protocol_version = @protocol_version,
                            os_platform = @os_platform,
                            architecture = @architecture,
                            last_seen_utc = @last_seen_utc,
                            last_connected_utc = @last_connected_utc,
                            created_utc = @created_utc,
                            last_update_utc = @last_update_utc
                            WHERE id = @id;";
                        BindHarbor(cmd, harbor);
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    await ReplaceCapabilitiesAsync(conn, tx, harbor, token).ConfigureAwait(false);
                    await tx.CommitAsync(token).ConfigureAwait(false);
                }
            }

            return harbor;
        }

        /// <inheritdoc />
        public async Task<Harbor?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync("SELECT * FROM harbors WHERE id = @id;", cmd => cmd.Parameters.AddWithValue("@id", id), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Harbor?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync("SELECT * FROM harbors WHERE tenant_id = @tenant_id AND id = @id;", cmd =>
            {
                cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                cmd.Parameters.AddWithValue("@id", id);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Harbor?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync("SELECT * FROM harbors WHERE tenant_id = @tenant_id AND user_id = @user_id AND id = @id;", cmd =>
            {
                cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                cmd.Parameters.AddWithValue("@user_id", userId);
                cmd.Parameters.AddWithValue("@id", id);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            await ExecuteDeleteAsync(id, cmd => cmd.Parameters.AddWithValue("@id", id), "DELETE FROM harbors WHERE id = @id;", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            await ExecuteDeleteAsync(id, cmd =>
            {
                cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                cmd.Parameters.AddWithValue("@id", id);
            }, "DELETE FROM harbors WHERE tenant_id = @tenant_id AND id = @id;", token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<Harbor>> EnumerateAsync(CancellationToken token = default)
        {
            return await EnumerateInternalAsync("SELECT * FROM harbors ORDER BY created_utc DESC;", null, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<Harbor>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return await EnumerateInternalAsync("SELECT * FROM harbors WHERE tenant_id = @tenant_id ORDER BY created_utc DESC;", cmd => cmd.Parameters.AddWithValue("@tenant_id", tenantId), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<Harbor>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
            return await EnumerateInternalAsync("SELECT * FROM harbors WHERE tenant_id = @tenant_id AND user_id = @user_id ORDER BY created_utc DESC;", cmd =>
            {
                cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                cmd.Parameters.AddWithValue("@user_id", userId);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAnyAsync(CancellationToken token = default)
        {
            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT 1 FROM harbors LIMIT 1;";
                    object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                    return result != null && result != DBNull.Value;
                }
            }
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM harbors WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false) ?? 0L);
                    return count > 0;
                }
            }
        }

        private async Task<Harbor?> ReadInternalAsync(string sql, Action<SqliteCommand> parameterize, CancellationToken token)
        {
            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                Harbor? harbor = null;
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize(cmd);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            harbor = HarborFromReader(reader);
                    }
                }

                if (harbor != null)
                    harbor.Capabilities = await LoadCapabilitiesAsync(conn, harbor.Id, token).ConfigureAwait(false);
                return harbor;
            }
        }

        private async Task<List<Harbor>> EnumerateInternalAsync(string sql, Action<SqliteCommand>? parameterize, CancellationToken token)
        {
            List<Harbor> results = new List<Harbor>();
            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize?.Invoke(cmd);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(HarborFromReader(reader));
                    }
                }

                foreach (Harbor harbor in results)
                    harbor.Capabilities = await LoadCapabilitiesAsync(conn, harbor.Id, token).ConfigureAwait(false);
            }

            return results;
        }

        private async Task ExecuteDeleteAsync(string id, Action<SqliteCommand> parameterize, string harborSql, CancellationToken token)
        {
            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteTransaction tx = (SqliteTransaction)await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                {
                    using (SqliteCommand capCmd = conn.CreateCommand())
                    {
                        capCmd.Transaction = tx;
                        capCmd.CommandText = "DELETE FROM harbor_capabilities WHERE harbor_id = @id;";
                        capCmd.Parameters.AddWithValue("@id", id);
                        await capCmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = harborSql;
                        parameterize(cmd);
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    await tx.CommitAsync(token).ConfigureAwait(false);
                }
            }
        }

        private async Task ReplaceCapabilitiesAsync(SqliteConnection conn, SqliteTransaction tx, Harbor harbor, CancellationToken token)
        {
            using (SqliteCommand del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM harbor_capabilities WHERE harbor_id = @harbor_id;";
                del.Parameters.AddWithValue("@harbor_id", harbor.Id);
                await del.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            if (harbor.Capabilities == null) return;
            foreach (HarborCapability capability in harbor.Capabilities)
            {
                using (SqliteCommand ins = conn.CreateCommand())
                {
                    ins.Transaction = tx;
                    ins.CommandText = @"INSERT OR REPLACE INTO harbor_capabilities (harbor_id, name, available, detail)
                        VALUES (@harbor_id, @name, @available, @detail);";
                    ins.Parameters.AddWithValue("@harbor_id", harbor.Id);
                    ins.Parameters.AddWithValue("@name", capability.Name);
                    ins.Parameters.AddWithValue("@available", capability.Available ? 1 : 0);
                    ins.Parameters.AddWithValue("@detail", (object?)capability.Detail ?? DBNull.Value);
                    await ins.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        private async Task<List<HarborCapability>> LoadCapabilitiesAsync(SqliteConnection conn, string harborId, CancellationToken token)
        {
            List<HarborCapability> capabilities = new List<HarborCapability>();
            using (SqliteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name, available, detail FROM harbor_capabilities WHERE harbor_id = @harbor_id ORDER BY name;";
                cmd.Parameters.AddWithValue("@harbor_id", harborId);
                using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        capabilities.Add(new HarborCapability
                        {
                            Name = reader["name"].ToString()!,
                            Available = (SqliteDatabaseDriver.NullableBool(reader, "available")) ?? true,
                            Detail = SqliteDatabaseDriver.NullableString(reader["detail"])
                        });
                    }
                }
            }

            return capabilities;
        }

        private static void BindHarbor(SqliteCommand cmd, Harbor harbor)
        {
            cmd.Parameters.AddWithValue("@id", harbor.Id);
            cmd.Parameters.AddWithValue("@tenant_id", (object?)harbor.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_id", (object?)harbor.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@name", harbor.Name);
            cmd.Parameters.AddWithValue("@connection_status", harbor.ConnectionStatus.ToString());
            cmd.Parameters.AddWithValue("@max_concurrent_jobs", harbor.MaxConcurrentJobs);
            cmd.Parameters.AddWithValue("@enabled", harbor.Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@protocol_version", (object?)harbor.ProtocolVersion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@os_platform", (object?)harbor.OsPlatform ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@architecture", (object?)harbor.Architecture ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@last_seen_utc", harbor.LastSeenUtc.HasValue ? (object)SqliteDatabaseDriver.ToIso8601(harbor.LastSeenUtc.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@last_connected_utc", harbor.LastConnectedUtc.HasValue ? (object)SqliteDatabaseDriver.ToIso8601(harbor.LastConnectedUtc.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@created_utc", SqliteDatabaseDriver.ToIso8601(harbor.CreatedUtc));
            cmd.Parameters.AddWithValue("@last_update_utc", SqliteDatabaseDriver.ToIso8601(harbor.LastUpdateUtc));
        }

        private static Harbor HarborFromReader(SqliteDataReader reader)
        {
            Harbor harbor = new Harbor
            {
                Id = reader["id"].ToString()!,
                TenantId = SqliteDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = SqliteDatabaseDriver.NullableString(reader["user_id"]),
                Name = reader["name"].ToString()!,
                ConnectionStatus = ParseEnum(reader["connection_status"], HarborConnectionStatusEnum.Unknown),
                MaxConcurrentJobs = SqliteDatabaseDriver.NullableInt(reader["max_concurrent_jobs"]) ?? 4,
                Enabled = SqliteDatabaseDriver.NullableBool(reader, "enabled") ?? true,
                ProtocolVersion = SqliteDatabaseDriver.NullableString(reader["protocol_version"]),
                OsPlatform = SqliteDatabaseDriver.NullableString(reader["os_platform"]),
                Architecture = SqliteDatabaseDriver.NullableString(reader["architecture"]),
                LastSeenUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["last_seen_utc"]),
                LastConnectedUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["last_connected_utc"]),
                CreatedUtc = SqliteDatabaseDriver.FromIso8601(reader["created_utc"].ToString()!),
                LastUpdateUtc = SqliteDatabaseDriver.FromIso8601(reader["last_update_utc"].ToString()!)
            };

            return harbor;
        }

        private static TEnum ParseEnum<TEnum>(object value, TEnum fallback) where TEnum : struct
        {
            if (value == null || value == DBNull.Value) return fallback;
            return Enum.TryParse<TEnum>(value.ToString(), true, out TEnum parsed) ? parsed : fallback;
        }
    }
}
