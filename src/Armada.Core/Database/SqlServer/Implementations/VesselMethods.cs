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
    /// SQL Server implementation of vessel database operations.
    /// </summary>
    public class VesselMethods : IVesselMethods
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private string _ConnectionString;
        private static readonly string _Iso8601Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate vessel methods for SQL Server.
        /// </summary>
        /// <param name="connectionString">SQL Server connection string.</param>
        public VesselMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a vessel.
        /// </summary>
        /// <param name="vessel">Vessel to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created vessel.</returns>
        public async Task<Vessel> CreateAsync(Vessel vessel, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            vessel.LastUpdateUtc = DateTime.UtcNow;

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO vessels (id, fleet_id, name, repo_url, local_path, working_directory, project_context, style_guide, landing_mode, default_branch, active, created_utc, last_update_utc)
                        VALUES (@id, @fleet_id, @name, @repo_url, @local_path, @working_directory, @project_context, @style_guide, @landing_mode, @default_branch, @active, @created_utc, @last_update_utc);";
                    cmd.Parameters.AddWithValue("@id", vessel.Id);
                    cmd.Parameters.AddWithValue("@fleet_id", (object?)vessel.FleetId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@name", vessel.Name);
                    cmd.Parameters.AddWithValue("@repo_url", (object?)vessel.RepoUrl ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@local_path", (object?)vessel.LocalPath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@working_directory", (object?)vessel.WorkingDirectory ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@project_context", (object?)vessel.ProjectContext ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@style_guide", (object?)vessel.StyleGuide ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@landing_mode", vessel.LandingMode.HasValue ? vessel.LandingMode.Value.ToString() : DBNull.Value);
                    cmd.Parameters.AddWithValue("@default_branch", vessel.DefaultBranch);
                    cmd.Parameters.AddWithValue("@active", vessel.Active);
                    cmd.Parameters.AddWithValue("@created_utc", ToIso8601(vessel.CreatedUtc));
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(vessel.LastUpdateUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return vessel;
        }

        /// <summary>
        /// Read a vessel by identifier.
        /// </summary>
        /// <param name="id">Vessel identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Vessel or null if not found.</returns>
        public async Task<Vessel?> ReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM vessels WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return VesselFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Read a vessel by name.
        /// </summary>
        /// <param name="name">Vessel name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Vessel or null if not found.</returns>
        public async Task<Vessel?> ReadByNameAsync(string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM vessels WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return VesselFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Update a vessel.
        /// </summary>
        /// <param name="vessel">Vessel to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated vessel.</returns>
        public async Task<Vessel> UpdateAsync(Vessel vessel, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            vessel.LastUpdateUtc = DateTime.UtcNow;

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE vessels SET
                        fleet_id = @fleet_id,
                        name = @name,
                        repo_url = @repo_url,
                        local_path = @local_path,
                        working_directory = @working_directory,
                        project_context = @project_context,
                        style_guide = @style_guide,
                        landing_mode = @landing_mode,
                        default_branch = @default_branch,
                        active = @active,
                        last_update_utc = @last_update_utc
                        WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", vessel.Id);
                    cmd.Parameters.AddWithValue("@fleet_id", (object?)vessel.FleetId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@name", vessel.Name);
                    cmd.Parameters.AddWithValue("@repo_url", (object?)vessel.RepoUrl ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@local_path", (object?)vessel.LocalPath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@working_directory", (object?)vessel.WorkingDirectory ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@project_context", (object?)vessel.ProjectContext ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@style_guide", (object?)vessel.StyleGuide ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@landing_mode", vessel.LandingMode.HasValue ? vessel.LandingMode.Value.ToString() : DBNull.Value);
                    cmd.Parameters.AddWithValue("@default_branch", vessel.DefaultBranch);
                    cmd.Parameters.AddWithValue("@active", vessel.Active);
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(vessel.LastUpdateUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return vessel;
        }

        /// <summary>
        /// Delete a vessel by identifier.
        /// </summary>
        /// <param name="id">Vessel identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM vessels WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Enumerate all vessels.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of all vessels.</returns>
        public async Task<List<Vessel>> EnumerateAsync(CancellationToken token = default)
        {
            List<Vessel> results = new List<Vessel>();

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM vessels ORDER BY name;";
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(VesselFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Enumerate vessels by fleet identifier.
        /// </summary>
        /// <param name="fleetId">Fleet identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of vessels in the fleet.</returns>
        public async Task<List<Vessel>> EnumerateByFleetAsync(string fleetId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(fleetId)) throw new ArgumentNullException(nameof(fleetId));
            List<Vessel> results = new List<Vessel>();

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM vessels WHERE fleet_id = @fleet_id ORDER BY name;";
                    cmd.Parameters.AddWithValue("@fleet_id", fleetId);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(VesselFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Enumerate vessels with pagination and filtering.
        /// </summary>
        /// <param name="query">Enumeration query parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated enumeration result.</returns>
        public async Task<EnumerationResult<Vessel>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
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
                if (!string.IsNullOrEmpty(query.FleetId))
                {
                    conditions.Add("fleet_id = @fleet_id");
                    parameters.Add(new SqlParameter("@fleet_id", query.FleetId));
                }

                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                // Count
                long totalCount = 0;
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM vessels" + whereClause + ";";
                    foreach (SqlParameter p in parameters) cmd.Parameters.Add(new SqlParameter(p.ParameterName, p.Value));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                // Query
                List<Vessel> results = new List<Vessel>();
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM vessels" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " OFFSET " + query.Offset + " ROWS FETCH NEXT " + query.PageSize + " ROWS ONLY;";
                    foreach (SqlParameter p in parameters) cmd.Parameters.Add(new SqlParameter(p.ParameterName, p.Value));
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(VesselFromReader(reader));
                    }
                }

                return EnumerationResult<Vessel>.Create(query, results, totalCount);
            }
        }

        /// <summary>
        /// Check if a vessel exists by identifier.
        /// </summary>
        /// <param name="id">Vessel identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the vessel exists.</returns>
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM vessels WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    int count = Convert.ToInt32(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
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

        private static Vessel VesselFromReader(SqlDataReader reader)
        {
            Vessel vessel = new Vessel();
            vessel.Id = reader["id"].ToString()!;
            vessel.FleetId = NullableString(reader["fleet_id"]);
            vessel.Name = reader["name"].ToString()!;
            vessel.RepoUrl = NullableString(reader["repo_url"]);
            vessel.LocalPath = NullableString(reader["local_path"]);
            vessel.WorkingDirectory = NullableString(reader["working_directory"]);
            vessel.ProjectContext = NullableString(reader["project_context"]);
            vessel.StyleGuide = NullableString(reader["style_guide"]);
            string? landingModeStr = NullableString(reader["landing_mode"]);
            if (!String.IsNullOrEmpty(landingModeStr) && Enum.TryParse<LandingModeEnum>(landingModeStr, out LandingModeEnum lm))
                vessel.LandingMode = lm;
            vessel.DefaultBranch = reader["default_branch"].ToString()!;
            vessel.Active = Convert.ToBoolean(reader["active"]);
            vessel.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            vessel.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
            return vessel;
        }

        #endregion
    }
}
