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
    /// MySQL implementation of background job persistence.
    /// </summary>
    public class JobMethods : IJobMethods
    {
        private readonly string _ConnectionString;

        /// <summary>
        /// Initializes a new instance of the <see cref="JobMethods"/> class.
        /// </summary>
        public JobMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        /// <inheritdoc />
        public async Task<Job> CreateAsync(Job job, CancellationToken token = default)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
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
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
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
                    cmd.Parameters.AddWithValue("@started_utc", job.StartedUtc.HasValue ? (object)MysqlDatabaseDriver.ToIso8601(job.StartedUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@completed_utc", job.CompletedUtc.HasValue ? (object)MysqlDatabaseDriver.ToIso8601(job.CompletedUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@last_update_utc", MysqlDatabaseDriver.ToIso8601(job.LastUpdateUtc));
                    int rows = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    return rows > 0;
                }
            }
        }

        /// <inheritdoc />
        public async Task<bool> TryHeartbeatAsync(string id, int minimumProgress, DateTime lastUpdateUtc, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE jobs SET
                        progress = CASE WHEN progress < @progress THEN @progress ELSE progress END,
                        last_update_utc = @last_update_utc
                        WHERE id = @id AND status = @running;";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@progress", Math.Clamp(minimumProgress, 0, 100));
                    cmd.Parameters.AddWithValue("@last_update_utc", MysqlDatabaseDriver.ToIso8601(lastUpdateUtc));
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
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
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
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
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
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM jobs WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false) ?? 0L);
                    return count > 0;
                }
            }
        }

        private async Task<Job?> ReadInternalAsync(string sql, Action<MySqlCommand> parameterize, CancellationToken token)
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
                            return JobFromReader(reader);
                    }
                }
            }

            return null;
        }

        private async Task<List<Job>> EnumerateInternalAsync(string sql, Action<MySqlCommand>? parameterize, CancellationToken token)
        {
            List<Job> results = new List<Job>();
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
                            results.Add(JobFromReader(reader));
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

        private static void BindJob(MySqlCommand cmd, Job job)
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
            cmd.Parameters.AddWithValue("@created_utc", MysqlDatabaseDriver.ToIso8601(job.CreatedUtc));
            cmd.Parameters.AddWithValue("@started_utc", job.StartedUtc.HasValue ? (object)MysqlDatabaseDriver.ToIso8601(job.StartedUtc.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@completed_utc", job.CompletedUtc.HasValue ? (object)MysqlDatabaseDriver.ToIso8601(job.CompletedUtc.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("@last_update_utc", MysqlDatabaseDriver.ToIso8601(job.LastUpdateUtc));
        }

        private static Job JobFromReader(MySqlDataReader reader)
        {
            Job job = new Job
            {
                Id = reader["id"].ToString()!,
                TenantId = MysqlDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = MysqlDatabaseDriver.NullableString(reader["user_id"]),
                Name = reader["name"].ToString()!,
                Progress = MysqlDatabaseDriver.NullableInt(reader["progress"]) ?? 0,
                ResultJson = MysqlDatabaseDriver.NullableString(reader["result_json"]),
                ErrorReason = MysqlDatabaseDriver.NullableString(reader["error_reason"]),
                CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc),
                StartedUtc = MysqlDatabaseDriver.FromIso8601Nullable(reader["started_utc"]),
                CompletedUtc = MysqlDatabaseDriver.FromIso8601Nullable(reader["completed_utc"]),
                LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc)
            };

            string? kindStr = MysqlDatabaseDriver.NullableString(reader["kind"]);
            if (!String.IsNullOrEmpty(kindStr) && Enum.TryParse<JobKindEnum>(kindStr, out JobKindEnum kind))
                job.Kind = kind;

            string? statusStr = MysqlDatabaseDriver.NullableString(reader["status"]);
            if (!String.IsNullOrEmpty(statusStr) && Enum.TryParse<JobStatusEnum>(statusStr, out JobStatusEnum status))
                job.Status = status;

            return job;
        }
    }
}
