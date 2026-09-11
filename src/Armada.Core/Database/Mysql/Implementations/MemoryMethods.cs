namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of durable memory persistence, including the memory_tags child table.
    /// </summary>
    public class MemoryMethods : IMemoryMethods
    {
        private readonly string _ConnectionString;

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryMethods"/> class.
        /// </summary>
        public MemoryMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        /// <inheritdoc />
        public async Task<Memory> CreateAsync(Memory memory, CancellationToken token = default)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlTransaction tx = await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                {
                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"INSERT INTO memories
                            (id, tenant_id, user_id, scope, type, topic, memory_key, summary, content, salience, version, source_kind, source_voyage_id, source_mission_id, source_vessel_id, source_detail, vessel_id, created_utc, last_update_utc)
                            VALUES
                            (@id, @tenant_id, @user_id, @scope, @type, @topic, @memory_key, @summary, @content, @salience, @version, @source_kind, @source_voyage_id, @source_mission_id, @source_vessel_id, @source_detail, @vessel_id, @created_utc, @last_update_utc);";
                        BindMemory(cmd, memory);
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    await ReplaceTagsAsync(conn, tx, memory, token).ConfigureAwait(false);
                    await tx.CommitAsync(token).ConfigureAwait(false);
                }
            }

            return memory;
        }

        /// <inheritdoc />
        public async Task<Memory> UpdateAsync(Memory memory, CancellationToken token = default)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlTransaction tx = await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                {
                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"UPDATE memories SET
                            tenant_id = @tenant_id,
                            user_id = @user_id,
                            scope = @scope,
                            type = @type,
                            topic = @topic,
                            memory_key = @memory_key,
                            summary = @summary,
                            content = @content,
                            salience = @salience,
                            version = @version,
                            source_kind = @source_kind,
                            source_voyage_id = @source_voyage_id,
                            source_mission_id = @source_mission_id,
                            source_vessel_id = @source_vessel_id,
                            source_detail = @source_detail,
                            vessel_id = @vessel_id,
                            created_utc = @created_utc,
                            last_update_utc = @last_update_utc
                            WHERE id = @id;";
                        BindMemory(cmd, memory);
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    await ReplaceTagsAsync(conn, tx, memory, token).ConfigureAwait(false);
                    await tx.CommitAsync(token).ConfigureAwait(false);
                }
            }

            return memory;
        }

        /// <inheritdoc />
        public async Task<Memory?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync(
                "SELECT * FROM memories WHERE id = @id;",
                cmd => cmd.Parameters.AddWithValue("@id", id),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Memory?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync(
                "SELECT * FROM memories WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Memory?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync(
                "SELECT * FROM memories WHERE tenant_id = @tenant_id AND user_id = @user_id AND id = @id;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@user_id", userId);
                    cmd.Parameters.AddWithValue("@id", id);
                },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Memory?> ReadByKeyAsync(string tenantId, string key, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));
            return await ReadInternalAsync(
                "SELECT * FROM memories WHERE tenant_id = @tenant_id AND memory_key = @memory_key ORDER BY created_utc DESC LIMIT 1;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@memory_key", key);
                },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            await ExecuteDeleteAsync(
                id,
                cmd => cmd.Parameters.AddWithValue("@id", id),
                "DELETE FROM memories WHERE id = @id;",
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            await ExecuteDeleteAsync(
                id,
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                },
                "DELETE FROM memories WHERE tenant_id = @tenant_id AND id = @id;",
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<Memory>> EnumerateAsync(CancellationToken token = default)
        {
            return await EnumerateInternalAsync(
                "SELECT * FROM memories ORDER BY created_utc DESC;",
                null,
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<Memory>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return await EnumerateInternalAsync(
                "SELECT * FROM memories WHERE tenant_id = @tenant_id ORDER BY created_utc DESC;",
                cmd => cmd.Parameters.AddWithValue("@tenant_id", tenantId),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<Memory>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
            return await EnumerateInternalAsync(
                "SELECT * FROM memories WHERE tenant_id = @tenant_id AND user_id = @user_id ORDER BY created_utc DESC;",
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
                    cmd.CommandText = "SELECT 1 FROM memories LIMIT 1;";
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
                    cmd.CommandText = "SELECT COUNT(*) FROM memories WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false) ?? 0L);
                    return count > 0;
                }
            }
        }

        private async Task<Memory?> ReadInternalAsync(string sql, Action<MySqlCommand> parameterize, CancellationToken token)
        {
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                Memory? memory = null;
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize(cmd);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            memory = MemoryFromReader(reader);
                    }
                }

                if (memory != null)
                    memory.Tags = await LoadTagsAsync(conn, memory.Id, token).ConfigureAwait(false);
                return memory;
            }
        }

        private async Task<List<Memory>> EnumerateInternalAsync(string sql, Action<MySqlCommand>? parameterize, CancellationToken token)
        {
            List<Memory> results = new List<Memory>();
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
                            results.Add(MemoryFromReader(reader));
                    }
                }

                foreach (Memory memory in results)
                    memory.Tags = await LoadTagsAsync(conn, memory.Id, token).ConfigureAwait(false);
            }

            return results;
        }

        private async Task ExecuteDeleteAsync(string id, Action<MySqlCommand> parameterize, string memorySql, CancellationToken token)
        {
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlTransaction tx = await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                {
                    using (MySqlCommand tagCmd = conn.CreateCommand())
                    {
                        tagCmd.Transaction = tx;
                        tagCmd.CommandText = "DELETE FROM memory_tags WHERE memory_id = @id;";
                        tagCmd.Parameters.AddWithValue("@id", id);
                        await tagCmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = memorySql;
                        parameterize(cmd);
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    await tx.CommitAsync(token).ConfigureAwait(false);
                }
            }
        }

        private async Task ReplaceTagsAsync(MySqlConnection conn, MySqlTransaction tx, Memory memory, CancellationToken token)
        {
            using (MySqlCommand del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM memory_tags WHERE memory_id = @memory_id;";
                del.Parameters.AddWithValue("@memory_id", memory.Id);
                await del.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            if (memory.Tags == null) return;
            foreach (string tag in memory.Tags)
            {
                if (String.IsNullOrWhiteSpace(tag)) continue;
                using (MySqlCommand ins = conn.CreateCommand())
                {
                    ins.Transaction = tx;
                    ins.CommandText = @"REPLACE INTO memory_tags (memory_id, tag) VALUES (@memory_id, @tag);";
                    ins.Parameters.AddWithValue("@memory_id", memory.Id);
                    ins.Parameters.AddWithValue("@tag", tag.Trim());
                    await ins.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        private async Task<List<string>> LoadTagsAsync(MySqlConnection conn, string memoryId, CancellationToken token)
        {
            List<string> tags = new List<string>();
            using (MySqlCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT tag FROM memory_tags WHERE memory_id = @memory_id ORDER BY tag;";
                cmd.Parameters.AddWithValue("@memory_id", memoryId);
                using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                        tags.Add(reader["tag"].ToString()!);
                }
            }

            return tags;
        }

        private static void BindMemory(MySqlCommand cmd, Memory memory)
        {
            cmd.Parameters.AddWithValue("@id", memory.Id);
            cmd.Parameters.AddWithValue("@tenant_id", (object?)memory.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_id", (object?)memory.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@scope", memory.Scope.ToString());
            cmd.Parameters.AddWithValue("@type", memory.Type.ToString());
            cmd.Parameters.AddWithValue("@topic", (object?)memory.Topic ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@memory_key", (object?)memory.Key ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@summary", (object?)memory.Summary ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@content", memory.Content ?? String.Empty);
            cmd.Parameters.AddWithValue("@salience", memory.Salience);
            cmd.Parameters.AddWithValue("@version", memory.Version);
            cmd.Parameters.AddWithValue("@source_kind", memory.SourceKind.ToString());
            cmd.Parameters.AddWithValue("@source_voyage_id", (object?)memory.SourceVoyageId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@source_mission_id", (object?)memory.SourceMissionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@source_vessel_id", (object?)memory.SourceVesselId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@source_detail", (object?)memory.SourceDetail ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vessel_id", (object?)memory.VesselId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@created_utc", MysqlDatabaseDriver.ToIso8601(memory.CreatedUtc));
            cmd.Parameters.AddWithValue("@last_update_utc", MysqlDatabaseDriver.ToIso8601(memory.LastUpdateUtc));
        }

        private static Memory MemoryFromReader(MySqlDataReader reader)
        {
            Memory memory = new Memory
            {
                Id = reader["id"].ToString()!,
                TenantId = MysqlDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = MysqlDatabaseDriver.NullableString(reader["user_id"]),
                Scope = ParseEnum(reader["scope"], ScopeEnum.TenantWide),
                Type = ParseEnum(reader["type"], MemoryTypeEnum.Semantic),
                Topic = MysqlDatabaseDriver.NullableString(reader["topic"]),
                Key = MysqlDatabaseDriver.NullableString(reader["memory_key"]),
                Summary = MysqlDatabaseDriver.NullableString(reader["summary"]),
                Content = MysqlDatabaseDriver.NullableString(reader["content"]) ?? String.Empty,
                Salience = reader["salience"] == DBNull.Value ? 0.5 : Convert.ToDouble(reader["salience"]),
                Version = MysqlDatabaseDriver.NullableInt(reader["version"]) ?? 1,
                SourceKind = ParseEnum(reader["source_kind"], MemorySourceKindEnum.Manual),
                SourceVoyageId = MysqlDatabaseDriver.NullableString(reader["source_voyage_id"]),
                SourceMissionId = MysqlDatabaseDriver.NullableString(reader["source_mission_id"]),
                SourceVesselId = MysqlDatabaseDriver.NullableString(reader["source_vessel_id"]),
                SourceDetail = MysqlDatabaseDriver.NullableString(reader["source_detail"]),
                VesselId = MysqlDatabaseDriver.NullableString(reader["vessel_id"]),
                CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc),
                LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc)
            };

            return memory;
        }

        private static TEnum ParseEnum<TEnum>(object value, TEnum fallback) where TEnum : struct
        {
            if (value == null || value == DBNull.Value) return fallback;
            return Enum.TryParse<TEnum>(value.ToString(), true, out TEnum parsed) ? parsed : fallback;
        }
    }
}
