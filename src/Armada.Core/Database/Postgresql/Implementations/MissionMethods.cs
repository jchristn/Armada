namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using SyslogLogging;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// PostgreSQL implementation of mission database operations.
    /// </summary>
    public class MissionMethods : IMissionMethods
    {
        #region Private-Members

        private string _Header = "[MissionMethods] ";
        private PostgresqlDatabaseDriver _Driver;
        private DatabaseSettings _Settings;
        private LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">PostgreSQL database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public MissionMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a mission.
        /// </summary>
        /// <param name="mission">Mission to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created mission.</returns>
        public async Task<Mission> CreateAsync(Mission mission, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            mission.LastUpdateUtc = DateTime.UtcNow;

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = @"INSERT INTO missions (id, voyage_id, vessel_id, captain_id, title, description,
                        status, priority, parent_mission_id, branch_name, dock_id, process_id,
                        pr_url, commit_hash, diff_snapshot, created_utc, started_utc, completed_utc, last_update_utc)
                        VALUES (@id, @voyage_id, @vessel_id, @captain_id, @title, @description,
                        @status, @priority, @parent_mission_id, @branch_name, @dock_id, @process_id,
                        @pr_url, @commit_hash, @diff_snapshot, @created_utc, @started_utc, @completed_utc, @last_update_utc);";
                    AddMissionParameters(cmd, mission);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return mission;
        }

        /// <summary>
        /// Read a mission by identifier.
        /// </summary>
        /// <param name="id">Mission identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Mission or null if not found.</returns>
        public async Task<Mission?> ReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM missions WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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
        /// <param name="mission">Mission to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated mission.</returns>
        public async Task<Mission> UpdateAsync(Mission mission, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            mission.LastUpdateUtc = DateTime.UtcNow;

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = @"UPDATE missions SET
                        voyage_id = @voyage_id, vessel_id = @vessel_id, captain_id = @captain_id,
                        title = @title, description = @description, status = @status,
                        priority = @priority, parent_mission_id = @parent_mission_id,
                        branch_name = @branch_name, dock_id = @dock_id, process_id = @process_id,
                        pr_url = @pr_url, commit_hash = @commit_hash, diff_snapshot = @diff_snapshot,
                        started_utc = @started_utc, completed_utc = @completed_utc,
                        last_update_utc = @last_update_utc
                        WHERE id = @id;";
                    AddMissionParameters(cmd, mission);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return mission;
        }

        /// <summary>
        /// Delete a mission by identifier.
        /// </summary>
        /// <param name="id">Mission identifier.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "DELETE FROM missions WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Enumerate all missions.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of all missions.</returns>
        public async Task<List<Mission>> EnumerateAsync(CancellationToken token = default)
        {
            List<Mission> results = new List<Mission>();

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM missions ORDER BY created_utc DESC;";
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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
        /// <param name="query">Enumeration query parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated enumeration result.</returns>
        public async Task<EnumerationResult<Mission>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) query = new EnumerationQuery();

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<NpgsqlParameter> parameters = new List<NpgsqlParameter>();

                if (query.CreatedAfter.HasValue)
                {
                    conditions.Add("created_utc > @created_after");
                    parameters.Add(new NpgsqlParameter("@created_after", query.CreatedAfter.Value));
                }
                if (query.CreatedBefore.HasValue)
                {
                    conditions.Add("created_utc < @created_before");
                    parameters.Add(new NpgsqlParameter("@created_before", query.CreatedBefore.Value));
                }
                if (!string.IsNullOrEmpty(query.Status))
                {
                    conditions.Add("status = @status");
                    parameters.Add(new NpgsqlParameter("@status", query.Status));
                }
                if (!string.IsNullOrEmpty(query.VoyageId))
                {
                    conditions.Add("voyage_id = @voyage_id");
                    parameters.Add(new NpgsqlParameter("@voyage_id", query.VoyageId));
                }
                if (!string.IsNullOrEmpty(query.VesselId))
                {
                    conditions.Add("vessel_id = @vessel_id");
                    parameters.Add(new NpgsqlParameter("@vessel_id", query.VesselId));
                }
                if (!string.IsNullOrEmpty(query.CaptainId))
                {
                    conditions.Add("captain_id = @captain_id");
                    parameters.Add(new NpgsqlParameter("@captain_id", query.CaptainId));
                }

                string whereClause = conditions.Count > 0
                    ? " WHERE " + string.Join(" AND ", conditions)
                    : "";
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                // Count
                long totalCount = 0;
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT COUNT(*) FROM missions" + whereClause + ";";
                    foreach (NpgsqlParameter p in parameters)
                        cmd.Parameters.Add(new NpgsqlParameter(p.ParameterName, p.Value));
                    totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                }

                // Query
                List<Mission> results = new List<Mission>();
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM missions" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (NpgsqlParameter p in parameters)
                        cmd.Parameters.Add(new NpgsqlParameter(p.ParameterName, p.Value));
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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
        /// <param name="voyageId">Voyage identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of missions for the voyage.</returns>
        public async Task<List<Mission>> EnumerateByVoyageAsync(string voyageId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(voyageId)) throw new ArgumentNullException(nameof(voyageId));
            return await EnumerateByColumnAsync("voyage_id", voyageId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Enumerate missions by vessel identifier.
        /// </summary>
        /// <param name="vesselId">Vessel identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of missions for the vessel.</returns>
        public async Task<List<Mission>> EnumerateByVesselAsync(string vesselId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            return await EnumerateByColumnAsync("vessel_id", vesselId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Enumerate missions by captain identifier.
        /// </summary>
        /// <param name="captainId">Captain identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of missions for the captain.</returns>
        public async Task<List<Mission>> EnumerateByCaptainAsync(string captainId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            return await EnumerateByColumnAsync("captain_id", captainId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Enumerate missions by status.
        /// </summary>
        /// <param name="status">Mission status to filter by.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of missions with the specified status.</returns>
        public async Task<List<Mission>> EnumerateByStatusAsync(MissionStatusEnum status, CancellationToken token = default)
        {
            return await EnumerateByColumnAsync("status", status.ToString(), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Check if a mission exists by identifier.
        /// </summary>
        /// <param name="id">Mission identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the mission exists.</returns>
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT COUNT(*) FROM missions WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                    return count > 0;
                }
            }
        }

        #endregion

        #region Private-Methods

        private static void AddMissionParameters(NpgsqlCommand cmd, Mission mission)
        {
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
            cmd.Parameters.AddWithValue("@created_utc", mission.CreatedUtc);
            cmd.Parameters.AddWithValue("@started_utc", mission.StartedUtc.HasValue ? (object)mission.StartedUtc.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@completed_utc", mission.CompletedUtc.HasValue ? (object)mission.CompletedUtc.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@last_update_utc", mission.LastUpdateUtc);
        }

        private async Task<List<Mission>> EnumerateByColumnAsync(string column, string value, CancellationToken token)
        {
            List<Mission> results = new List<Mission>();

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM missions WHERE " + column + " = @value ORDER BY created_utc DESC;";
                    cmd.Parameters.AddWithValue("@value", value);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(MissionFromReader(reader));
                    }
                }
            }

            return results;
        }

        private static Mission MissionFromReader(NpgsqlDataReader reader)
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
            mission.CreatedUtc = ((DateTime)reader["created_utc"]).ToUniversalTime();
            mission.StartedUtc = NullableDateTime(reader["started_utc"]);
            mission.CompletedUtc = NullableDateTime(reader["completed_utc"]);
            mission.LastUpdateUtc = ((DateTime)reader["last_update_utc"]).ToUniversalTime();
            return mission;
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

        private static DateTime? NullableDateTime(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            return ((DateTime)value).ToUniversalTime();
        }

        #endregion
    }
}
