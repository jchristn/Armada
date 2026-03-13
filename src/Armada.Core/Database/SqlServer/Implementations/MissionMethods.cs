namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// SQL Server implementation of mission database operations.
    /// </summary>
    internal class MissionMethods : IMissionMethods
    {
        #region Private-Members

        private string _ConnectionString;
        private static readonly string _Iso8601Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">SQL Server connection string.</param>
        internal MissionMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a mission.
        /// </summary>
        public async Task<Mission> CreateAsync(Mission mission, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            mission.LastUpdateUtc = DateTime.UtcNow;

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO missions (id, voyage_id, vessel_id, captain_id, title, description, status, priority, parent_mission_id, branch_name, dock_id, process_id, pr_url, commit_hash, diff_snapshot, created_utc, started_utc, completed_utc, last_update_utc)
                        VALUES (@id, @voyage_id, @vessel_id, @captain_id, @title, @description, @status, @priority, @parent_mission_id, @branch_name, @dock_id, @process_id, @pr_url, @commit_hash, @diff_snapshot, @created_utc, @started_utc, @completed_utc, @last_update_utc);";
                    cmd.Parameters.AddWithValue("@id", mission.Id);
                    cmd.Parameters.AddWithValue("@voyage_id", (object?)mission.VoyageId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@vessel_id", (object?)mission.VesselId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@captain_id", (object?)mission.CaptainId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@title", mission.Title);
                    cmd.Parameters.AddWithValue("@description", (object?)mission.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@status", mission.Status.ToString());
                    cmd.Parameters.AddWithValue("@priority", mission.Priority);
                    cmd.Parameters.AddWithValue("@parent_mission_id", (object?)mission.ParentMissionId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@branch_name", (object?)mission.BranchName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@dock_id", (object?)mission.DockId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@process_id", mission.ProcessId.HasValue ? (object)mission.ProcessId.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@pr_url", (object?)mission.PrUrl ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@commit_hash", (object?)mission.CommitHash ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@diff_snapshot", (object?)mission.DiffSnapshot ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@created_utc", ToIso8601(mission.CreatedUtc));
                    cmd.Parameters.AddWithValue("@started_utc", mission.StartedUtc.HasValue ? (object)ToIso8601(mission.StartedUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@completed_utc", mission.CompletedUtc.HasValue ? (object)ToIso8601(mission.CompletedUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(mission.LastUpdateUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return mission;
        }

        /// <summary>
        /// Read a mission by identifier.
        /// </summary>
        public async Task<Mission?> ReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM missions WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return MissionFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Update a mission.
        /// </summary>
        public async Task<Mission> UpdateAsync(Mission mission, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            mission.LastUpdateUtc = DateTime.UtcNow;

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE missions SET
                        voyage_id = @voyage_id,
                        vessel_id = @vessel_id,
                        captain_id = @captain_id,
                        title = @title,
                        description = @description,
                        status = @status,
                        priority = @priority,
                        parent_mission_id = @parent_mission_id,
                        branch_name = @branch_name,
                        dock_id = @dock_id,
                        process_id = @process_id,
                        pr_url = @pr_url,
                        commit_hash = @commit_hash,
                        diff_snapshot = @diff_snapshot,
                        started_utc = @started_utc,
                        completed_utc = @completed_utc,
                        last_update_utc = @last_update_utc
                        WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", mission.Id);
                    cmd.Parameters.AddWithValue("@voyage_id", (object?)mission.VoyageId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@vessel_id", (object?)mission.VesselId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@captain_id", (object?)mission.CaptainId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@title", mission.Title);
                    cmd.Parameters.AddWithValue("@description", (object?)mission.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@status", mission.Status.ToString());
                    cmd.Parameters.AddWithValue("@priority", mission.Priority);
                    cmd.Parameters.AddWithValue("@parent_mission_id", (object?)mission.ParentMissionId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@branch_name", (object?)mission.BranchName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@dock_id", (object?)mission.DockId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@process_id", mission.ProcessId.HasValue ? (object)mission.ProcessId.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@pr_url", (object?)mission.PrUrl ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@commit_hash", (object?)mission.CommitHash ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@diff_snapshot", (object?)mission.DiffSnapshot ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@started_utc", mission.StartedUtc.HasValue ? (object)ToIso8601(mission.StartedUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@completed_utc", mission.CompletedUtc.HasValue ? (object)ToIso8601(mission.CompletedUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(mission.LastUpdateUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return mission;
        }

        /// <summary>
        /// Delete a mission by identifier.
        /// </summary>
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM missions WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Enumerate all missions.
        /// </summary>
        public async Task<List<Mission>> EnumerateAsync(CancellationToken token = default)
        {
            List<Mission> results = new List<Mission>();

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM missions ORDER BY priority ASC, created_utc ASC;";
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(MissionFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Enumerate missions with pagination and filtering.
        /// </summary>
        public async Task<EnumerationResult<Mission>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) query = new EnumerationQuery();

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<SqlParameter> parameters = new List<SqlParameter>();

                if (query.CreatedAfter.HasValue)
                {
                    conditions.Add("created_utc > @created_after");
                    parameters.Add(new SqlParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                }
                if (query.CreatedBefore.HasValue)
                {
                    conditions.Add("created_utc < @created_before");
                    parameters.Add(new SqlParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                }
                if (!string.IsNullOrEmpty(query.Status))
                {
                    conditions.Add("status = @status");
                    parameters.Add(new SqlParameter("@status", query.Status));
                }
                if (!string.IsNullOrEmpty(query.VoyageId))
                {
                    conditions.Add("voyage_id = @voyage_id");
                    parameters.Add(new SqlParameter("@voyage_id", query.VoyageId));
                }
                if (!string.IsNullOrEmpty(query.VesselId))
                {
                    conditions.Add("vessel_id = @vessel_id");
                    parameters.Add(new SqlParameter("@vessel_id", query.VesselId));
                }
                if (!string.IsNullOrEmpty(query.CaptainId))
                {
                    conditions.Add("captain_id = @captain_id");
                    parameters.Add(new SqlParameter("@captain_id", query.CaptainId));
                }

                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                // Count
                int totalCount = 0;
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM missions" + whereClause + ";";
                    foreach (SqlParameter p in parameters) cmd.Parameters.Add(new SqlParameter(p.ParameterName, p.Value));
                    totalCount = (int)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                }

                // Query
                List<Mission> results = new List<Mission>();
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM missions" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " OFFSET " + query.Offset + " ROWS FETCH NEXT " + query.PageSize + " ROWS ONLY;";
                    foreach (SqlParameter p in parameters) cmd.Parameters.Add(new SqlParameter(p.ParameterName, p.Value));
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(MissionFromReader(reader));
                    }
                }

                return EnumerationResult<Mission>.Create(query, results, totalCount);
            }
        }

        /// <summary>
        /// Enumerate missions by voyage identifier.
        /// </summary>
        public async Task<List<Mission>> EnumerateByVoyageAsync(string voyageId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(voyageId)) throw new ArgumentNullException(nameof(voyageId));
            List<Mission> results = new List<Mission>();

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM missions WHERE voyage_id = @voyage_id ORDER BY priority ASC, created_utc ASC;";
                    cmd.Parameters.AddWithValue("@voyage_id", voyageId);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(MissionFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Enumerate missions by vessel identifier.
        /// </summary>
        public async Task<List<Mission>> EnumerateByVesselAsync(string vesselId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            List<Mission> results = new List<Mission>();

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM missions WHERE vessel_id = @vessel_id ORDER BY priority ASC, created_utc ASC;";
                    cmd.Parameters.AddWithValue("@vessel_id", vesselId);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(MissionFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Enumerate missions by captain identifier.
        /// </summary>
        public async Task<List<Mission>> EnumerateByCaptainAsync(string captainId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            List<Mission> results = new List<Mission>();

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM missions WHERE captain_id = @captain_id ORDER BY priority ASC, created_utc ASC;";
                    cmd.Parameters.AddWithValue("@captain_id", captainId);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(MissionFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Enumerate missions by status.
        /// </summary>
        public async Task<List<Mission>> EnumerateByStatusAsync(MissionStatusEnum status, CancellationToken token = default)
        {
            List<Mission> results = new List<Mission>();

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM missions WHERE status = @status ORDER BY priority ASC, created_utc ASC;";
                    cmd.Parameters.AddWithValue("@status", status.ToString());
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(MissionFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Check if a mission exists by identifier.
        /// </summary>
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM missions WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    int count = (int)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                    return count > 0;
                }
            }
        }

        #endregion

        #region Private-Methods

        private static Mission MissionFromReader(SqlDataReader reader)
        {
            Mission mission = new Mission();
            mission.Id = reader["id"].ToString()!;
            mission.VoyageId = NullableString(reader["voyage_id"]);
            mission.VesselId = NullableString(reader["vessel_id"]);
            mission.CaptainId = NullableString(reader["captain_id"]);
            mission.Title = reader["title"].ToString()!;
            mission.Description = NullableString(reader["description"]);
            mission.Status = Enum.Parse<MissionStatusEnum>(reader["status"].ToString()!);
            mission.Priority = Convert.ToInt32(reader["priority"]);
            mission.ParentMissionId = NullableString(reader["parent_mission_id"]);
            mission.BranchName = NullableString(reader["branch_name"]);
            mission.DockId = NullableString(reader["dock_id"]);
            mission.ProcessId = NullableInt(reader["process_id"]);
            mission.PrUrl = NullableString(reader["pr_url"]);
            mission.CommitHash = NullableString(reader["commit_hash"]);
            mission.DiffSnapshot = NullableString(reader["diff_snapshot"]);
            mission.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            mission.StartedUtc = FromIso8601Nullable(reader["started_utc"]);
            mission.CompletedUtc = FromIso8601Nullable(reader["completed_utc"]);
            mission.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
            return mission;
        }

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

        #endregion
    }
}
