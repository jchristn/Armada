namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// PostgreSQL implementation of background job persistence.
    /// </summary>
    public class JobMethods : IJobMethods
    {
        private readonly PostgresqlDatabaseDriver _Driver;

        /// <summary>
        /// Initializes a new instance of the <see cref="JobMethods"/> class.
        /// </summary>
        public JobMethods(PostgresqlDatabaseDriver driver, Settings.DatabaseSettings settings, SyslogLogging.LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc />
        public async Task<Job> CreateAsync(Job job, CancellationToken token = default)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO jobs
                        (id, tenant_id, user_id, name, kind, status, progress, result_json, error_reason, created_utc, started_utc, completed_utc, last_update_utc)
                        VALUES
                        (@id, @tenant_id, @user_id, @name, @kind, @status, @progress, @result_json, @error_reason, @created_utc, @started_utc, @completed_utc, @last_update_utc);";
                    BindJob(cmd, job);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return job;
        }

        /// <inheritdoc />
        public async Task<Job> UpdateAsync(Job job, CancellationToken token = default)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE jobs SET
                        tenant_id = @tenant_id,
                        user_id = @user_id,
                        name = @name,
                        kind = @kind,
                        status = @status,
                        progress = @progress,
                        result_json = @result_json,
                        error_reason = @error_reason,
                        created_utc = @created_utc,
                        started_utc = @started_utc,
                        completed_utc = @completed_utc,
                        last_update_utc = @last_update_utc
                        WHERE id = @id;";
                    BindJob(cmd, job);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return job;
        }

        /// <inheritdoc />
        public async Task<bool> TryUpdateIfStatusAsync(Job job, IReadOnlyCollection<JobStatusEnum> expectedStatuses, CancellationToken token = default)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (expectedStatuses == null) throw new ArgumentNullException(nameof(expectedStatuses));
            if (expectedStatuses.Count == 0) throw new ArgumentException("At least one expected status is required.", nameof(expectedStatuses));

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    List<string> placeholders = new List<string>();
                    int index = 0;
                    foreach (JobStatusEnum expected in expectedStatuses)
                    {
                        string name = "@expected_" + index++;
                        placeholders.Add(name);
                        cmd.Parameters.AddWithValue(name, expected.ToString());
                    }

                    cmd.CommandText = @"UPDATE jobs SET
                        status = @status,
                        progress = @progress,
                        result_json = @result_json,
                        error_reason = @error_reason,
                        started_utc = @started_utc,
                        completed_utc = @completed_utc,
                        last_update_utc = @last_update_utc
                        WHERE id = @id AND status IN (" + String.Join(", ", placeholders) + ");";
                    cmd.Parameters.AddWithValue("@id", job.Id);
                    cmd.Parameters.AddWithValue("@status", job.Status.ToString());
                    cmd.Parameters.AddWithValue("@progress", job.Progress);
                    cmd.Parameters.AddWithValue("@result_json", (object?)job.ResultJson ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@error_reason", (object?)job.ErrorReason ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@started_utc", job.StartedUtc.HasValue ? (object)job.StartedUtc.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@completed_utc", job.CompletedUtc.HasValue ? (object)job.CompletedUtc.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@last_update_utc", job.LastUpdateUtc);
                    int rows = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    return rows > 0;
                }
            }
        }

        /// <inheritdoc />
        public async Task<bool> TryHeartbeatAsync(string id, int minimumProgress, DateTime lastUpdateUtc, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE jobs SET
                        progress = CASE WHEN progress < @progress THEN @progress ELSE progress END,
                        last_update_utc = @last_update_utc
                        WHERE id = @id AND status = @running;";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@progress", Math.Clamp(minimumProgress, 0, 100));
                    cmd.Parameters.AddWithValue("@last_update_utc", lastUpdateUtc);
                    cmd.Parameters.AddWithValue("@running", JobStatusEnum.Running.ToString());
                    int rows = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    return rows > 0;
                }
            }
        }

        /// <inheritdoc />
        public async Task<Job?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync(
                "SELECT * FROM jobs WHERE id = @id;",
                cmd => cmd.Parameters.AddWithValue("@id", id),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Job?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync(
                "SELECT * FROM jobs WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Job?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            return await ReadInternalAsync(
                "SELECT * FROM jobs WHERE tenant_id = @tenant_id AND user_id = @user_id AND id = @id;",
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
                "DELETE FROM jobs WHERE id = @id;",
                cmd => cmd.Parameters.AddWithValue("@id", id),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            await ExecuteDeleteAsync(
                "DELETE FROM jobs WHERE tenant_id = @tenant_id AND id = @id;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<Job>> EnumerateAsync(CancellationToken token = default)
        {
            return await EnumerateInternalAsync(
                "SELECT * FROM jobs ORDER BY created_utc DESC;",
                null,
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<Job>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return await EnumerateInternalAsync(
                "SELECT * FROM jobs WHERE tenant_id = @tenant_id ORDER BY created_utc DESC;",
                cmd => cmd.Parameters.AddWithValue("@tenant_id", tenantId),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<Job>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
            return await EnumerateInternalAsync(
                "SELECT * FROM jobs WHERE tenant_id = @tenant_id AND user_id = @user_id ORDER BY created_utc DESC;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@user_id", userId);
                },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Job>> EnumeratePageAsync(JobQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            string where = query.BuildWhereClause(out List<KeyValuePair<string, object>> parameters);

            long total;
            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM jobs" + where + ";";
                    foreach (KeyValuePair<string, object> parameter in parameters) cmd.Parameters.AddWithValue(parameter.Key, parameter.Value);
                    total = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false) ?? 0L);
                }
            }

            List<Job> page = await EnumerateInternalAsync("SELECT * FROM jobs" + where + " ORDER BY created_utc DESC LIMIT @page_size OFFSET @offset;", cmd =>
            {
                foreach (KeyValuePair<string, object> parameter in parameters) cmd.Parameters.AddWithValue(parameter.Key, parameter.Value);
                cmd.Parameters.AddWithValue("@page_size", query.PageSize);
                cmd.Parameters.AddWithValue("@offset", query.Offset);
            }, token).ConfigureAwait(false);
            return query.ToResult(page, total);
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAnyAsync(CancellationToken token = default)
        {
            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT 1 FROM jobs LIMIT 1;";
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
                    cmd.CommandText = "SELECT COUNT(*) FROM jobs WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false) ?? 0L);
                    return count > 0;
                }
            }
        }

        private async Task<Job?> ReadInternalAsync(string sql, Action<NpgsqlCommand> parameterize, CancellationToken token)
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
                            return JobFromReader(reader);
                    }
                }
            }

            return null;
        }

        private async Task<List<Job>> EnumerateInternalAsync(string sql, Action<NpgsqlCommand>? parameterize, CancellationToken token)
        {
            List<Job> results = new List<Job>();
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
                            results.Add(JobFromReader(reader));
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

        private static void BindJob(NpgsqlCommand cmd, Job job)
        {
            cmd.Parameters.AddWithValue("@id", job.Id);
            cmd.Parameters.AddWithValue("@tenant_id", (object?)job.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_id", (object?)job.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@name", job.Name);
            cmd.Parameters.AddWithValue("@kind", job.Kind.ToString());
            cmd.Parameters.AddWithValue("@status", job.Status.ToString());
            cmd.Parameters.AddWithValue("@progress", job.Progress);
            cmd.Parameters.AddWithValue("@result_json", (object?)job.ResultJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@error_reason", (object?)job.ErrorReason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@created_utc", job.CreatedUtc);
            cmd.Parameters.AddWithValue("@started_utc", (object?)job.StartedUtc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@completed_utc", (object?)job.CompletedUtc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@last_update_utc", job.LastUpdateUtc);
        }

        private static Job JobFromReader(NpgsqlDataReader reader)
        {
            Job job = new Job
            {
                Id = reader["id"].ToString()!,
                TenantId = NullableString(reader["tenant_id"]),
                UserId = NullableString(reader["user_id"]),
                Name = reader["name"].ToString()!,
                Kind = ParseEnum(reader["kind"], JobKindEnum.Generic),
                Status = ParseEnum(reader["status"], JobStatusEnum.Queued),
                Progress = Convert.ToInt32(reader["progress"]),
                ResultJson = NullableString(reader["result_json"]),
                ErrorReason = NullableString(reader["error_reason"]),
                CreatedUtc = (DateTime)reader["created_utc"],
                StartedUtc = NullableDateTime(reader["started_utc"]),
                CompletedUtc = NullableDateTime(reader["completed_utc"]),
                LastUpdateUtc = (DateTime)reader["last_update_utc"]
            };

            return job;
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
    }
}
