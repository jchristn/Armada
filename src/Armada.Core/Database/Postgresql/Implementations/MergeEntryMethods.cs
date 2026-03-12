namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// PostgreSQL implementation of merge entry database operations.
    /// </summary>
    public class MergeEntryMethods : IMergeEntryMethods
    {
        #region Private-Members

        private NpgsqlDataSource _DataSource;
        private static readonly string _Iso8601Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the PostgreSQL merge entry methods.
        /// </summary>
        /// <param name="dataSource">NpgsqlDataSource instance.</param>
        public MergeEntryMethods(NpgsqlDataSource dataSource)
        {
            _DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a merge entry.
        /// </summary>
        /// <param name="entry">Merge entry to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created merge entry.</returns>
        public async Task<MergeEntry> CreateAsync(MergeEntry entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            entry.LastUpdateUtc = DateTime.UtcNow;

            using (NpgsqlConnection conn = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            {
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = @"INSERT INTO merge_entries (id, mission_id, vessel_id, branch_name, target_branch, status, priority, batch_id, test_command, test_output, test_exit_code, created_utc, last_update_utc, test_started_utc, completed_utc)
                        VALUES (@id, @mission_id, @vessel_id, @branch_name, @target_branch, @status, @priority, @batch_id, @test_command, @test_output, @test_exit_code, @created_utc, @last_update_utc, @test_started_utc, @completed_utc);";
                    cmd.Parameters.AddWithValue("@id", entry.Id);
                    cmd.Parameters.AddWithValue("@mission_id", (object?)entry.MissionId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@vessel_id", (object?)entry.VesselId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@branch_name", entry.BranchName);
                    cmd.Parameters.AddWithValue("@target_branch", entry.TargetBranch);
                    cmd.Parameters.AddWithValue("@status", entry.Status.ToString());
                    cmd.Parameters.AddWithValue("@priority", entry.Priority);
                    cmd.Parameters.AddWithValue("@batch_id", (object?)entry.BatchId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@test_command", (object?)entry.TestCommand ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@test_output", (object?)entry.TestOutput ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@test_exit_code", entry.TestExitCode.HasValue ? (object)entry.TestExitCode.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@created_utc", ToIso8601(entry.CreatedUtc));
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(entry.LastUpdateUtc));
                    cmd.Parameters.AddWithValue("@test_started_utc", entry.TestStartedUtc.HasValue ? (object)ToIso8601(entry.TestStartedUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@completed_utc", entry.CompletedUtc.HasValue ? (object)ToIso8601(entry.CompletedUtc.Value) : DBNull.Value);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return entry;
        }

        /// <summary>
        /// Read a merge entry by identifier.
        /// </summary>
        /// <param name="id">Merge entry identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Merge entry if found, null otherwise.</returns>
        public async Task<MergeEntry?> ReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (NpgsqlConnection conn = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            {
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM merge_entries WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return MergeEntryFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Update a merge entry.
        /// </summary>
        /// <param name="entry">Merge entry with updated values.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated merge entry.</returns>
        public async Task<MergeEntry> UpdateAsync(MergeEntry entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            entry.LastUpdateUtc = DateTime.UtcNow;

            using (NpgsqlConnection conn = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            {
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = @"UPDATE merge_entries SET
                        mission_id = @mission_id,
                        vessel_id = @vessel_id,
                        branch_name = @branch_name,
                        target_branch = @target_branch,
                        status = @status,
                        priority = @priority,
                        batch_id = @batch_id,
                        test_command = @test_command,
                        test_output = @test_output,
                        test_exit_code = @test_exit_code,
                        last_update_utc = @last_update_utc,
                        test_started_utc = @test_started_utc,
                        completed_utc = @completed_utc
                        WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", entry.Id);
                    cmd.Parameters.AddWithValue("@mission_id", (object?)entry.MissionId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@vessel_id", (object?)entry.VesselId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@branch_name", entry.BranchName);
                    cmd.Parameters.AddWithValue("@target_branch", entry.TargetBranch);
                    cmd.Parameters.AddWithValue("@status", entry.Status.ToString());
                    cmd.Parameters.AddWithValue("@priority", entry.Priority);
                    cmd.Parameters.AddWithValue("@batch_id", (object?)entry.BatchId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@test_command", (object?)entry.TestCommand ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@test_output", (object?)entry.TestOutput ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@test_exit_code", entry.TestExitCode.HasValue ? (object)entry.TestExitCode.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(entry.LastUpdateUtc));
                    cmd.Parameters.AddWithValue("@test_started_utc", entry.TestStartedUtc.HasValue ? (object)ToIso8601(entry.TestStartedUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@completed_utc", entry.CompletedUtc.HasValue ? (object)ToIso8601(entry.CompletedUtc.Value) : DBNull.Value);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return entry;
        }

        /// <summary>
        /// Delete a merge entry by identifier.
        /// </summary>
        /// <param name="id">Merge entry identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (NpgsqlConnection conn = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            {
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "DELETE FROM merge_entries WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Enumerate all merge entries.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of all merge entries ordered by priority and creation date.</returns>
        public async Task<List<MergeEntry>> EnumerateAsync(CancellationToken token = default)
        {
            List<MergeEntry> results = new List<MergeEntry>();

            using (NpgsqlConnection conn = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            {
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM merge_entries ORDER BY priority ASC, created_utc ASC;";
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(MergeEntryFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Enumerate merge entries with pagination and filtering.
        /// </summary>
        /// <param name="query">Enumeration query with pagination and filter parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated enumeration result of merge entries.</returns>
        public async Task<EnumerationResult<MergeEntry>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) query = new EnumerationQuery();

            using (NpgsqlConnection conn = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            {
                List<string> conditions = new List<string>();
                List<NpgsqlParameter> parameters = new List<NpgsqlParameter>();

                if (query.CreatedAfter.HasValue)
                {
                    conditions.Add("created_utc > @created_after");
                    parameters.Add(new NpgsqlParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                }
                if (query.CreatedBefore.HasValue)
                {
                    conditions.Add("created_utc < @created_before");
                    parameters.Add(new NpgsqlParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                }
                if (!string.IsNullOrEmpty(query.Status))
                {
                    conditions.Add("status = @status");
                    parameters.Add(new NpgsqlParameter("@status", query.Status));
                }
                if (!string.IsNullOrEmpty(query.VesselId))
                {
                    conditions.Add("vessel_id = @vessel_id");
                    parameters.Add(new NpgsqlParameter("@vessel_id", query.VesselId));
                }
                if (!string.IsNullOrEmpty(query.MissionId))
                {
                    conditions.Add("mission_id = @mission_id");
                    parameters.Add(new NpgsqlParameter("@mission_id", query.MissionId));
                }

                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                // Count
                long totalCount = 0;
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT COUNT(*) FROM merge_entries" + whereClause + ";";
                    foreach (NpgsqlParameter p in parameters) cmd.Parameters.Add(new NpgsqlParameter(p.ParameterName, p.NpgsqlDbType) { Value = p.Value });
                    totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                }

                // Query
                List<MergeEntry> results = new List<MergeEntry>();
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM merge_entries" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (NpgsqlParameter p in parameters) cmd.Parameters.Add(new NpgsqlParameter(p.ParameterName, p.NpgsqlDbType) { Value = p.Value });
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(MergeEntryFromReader(reader));
                    }
                }

                return EnumerationResult<MergeEntry>.Create(query, results, totalCount);
            }
        }

        /// <summary>
        /// Enumerate merge entries by status.
        /// </summary>
        /// <param name="status">Merge status to filter by.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of merge entries with the specified status.</returns>
        public async Task<List<MergeEntry>> EnumerateByStatusAsync(MergeStatusEnum status, CancellationToken token = default)
        {
            List<MergeEntry> results = new List<MergeEntry>();

            using (NpgsqlConnection conn = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            {
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM merge_entries WHERE status = @status ORDER BY priority ASC, created_utc ASC;";
                    cmd.Parameters.AddWithValue("@status", status.ToString());
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(MergeEntryFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Check if a merge entry exists by identifier.
        /// </summary>
        /// <param name="id">Merge entry identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the merge entry exists.</returns>
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (NpgsqlConnection conn = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            {
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT COUNT(*) FROM merge_entries WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                    return count > 0;
                }
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
            return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
        }

        private static DateTime? FromIso8601Nullable(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            if (string.IsNullOrEmpty(str)) return null;
            return FromIso8601(str);
        }

        private static string? NullableString(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            return string.IsNullOrEmpty(str) ? null : str;
        }

        private static int? NullableInt(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            return Convert.ToInt32(value);
        }

        private static MergeEntry MergeEntryFromReader(NpgsqlDataReader reader)
        {
            MergeEntry entry = new MergeEntry();
            entry.Id = reader["id"].ToString()!;
            entry.MissionId = NullableString(reader["mission_id"]);
            entry.VesselId = NullableString(reader["vessel_id"]);
            entry.BranchName = reader["branch_name"].ToString()!;
            entry.TargetBranch = reader["target_branch"].ToString()!;
            entry.Status = Enum.Parse<MergeStatusEnum>(reader["status"].ToString()!);
            entry.Priority = Convert.ToInt32(reader["priority"]);
            entry.BatchId = NullableString(reader["batch_id"]);
            entry.TestCommand = NullableString(reader["test_command"]);
            entry.TestOutput = NullableString(reader["test_output"]);
            entry.TestExitCode = NullableInt(reader["test_exit_code"]);
            entry.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            entry.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
            entry.TestStartedUtc = FromIso8601Nullable(reader["test_started_utc"]);
            entry.CompletedUtc = FromIso8601Nullable(reader["completed_utc"]);
            return entry;
        }

        #endregion
    }
}
