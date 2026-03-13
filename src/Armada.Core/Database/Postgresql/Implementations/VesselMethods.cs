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
    /// PostgreSQL implementation of vessel database operations.
    /// </summary>
    public class VesselMethods : IVesselMethods
    {
        #region Private-Members

#pragma warning disable CS0414
        private string _Header = "[VesselMethods] ";
#pragma warning restore CS0414
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
        public VesselMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
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

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
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
                    cmd.Parameters.AddWithValue("@created_utc", vessel.CreatedUtc);
                    cmd.Parameters.AddWithValue("@last_update_utc", vessel.LastUpdateUtc);
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

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM vessels WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM vessels WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
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
                    cmd.Parameters.AddWithValue("@last_update_utc", vessel.LastUpdateUtc);
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
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
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

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM vessels ORDER BY name;";
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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
                if (!string.IsNullOrEmpty(query.FleetId))
                {
                    conditions.Add("fleet_id = @fleet_id");
                    parameters.Add(new NpgsqlParameter("@fleet_id", query.FleetId));
                }

                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                // Count
                long totalCount = 0;
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT COUNT(*) FROM vessels" + whereClause + ";";
                    foreach (NpgsqlParameter p in parameters) cmd.Parameters.Add(new NpgsqlParameter(p.ParameterName, p.Value));
                    totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                }

                // Query
                List<Vessel> results = new List<Vessel>();
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM vessels" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (NpgsqlParameter p in parameters) cmd.Parameters.Add(new NpgsqlParameter(p.ParameterName, p.Value));
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(VesselFromReader(reader));
                    }
                }

                return EnumerationResult<Vessel>.Create(query, results, totalCount);
            }
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

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM vessels WHERE fleet_id = @fleet_id ORDER BY name;";
                    cmd.Parameters.AddWithValue("@fleet_id", fleetId);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(VesselFromReader(reader));
                    }
                }
            }

            return results;
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

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT COUNT(*) FROM vessels WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                    return count > 0;
                }
            }
        }

        #endregion

        #region Private-Methods

        private static Vessel VesselFromReader(NpgsqlDataReader reader)
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
            vessel.Active = (bool)reader["active"];
            vessel.CreatedUtc = ((DateTime)reader["created_utc"]).ToUniversalTime();
            vessel.LastUpdateUtc = ((DateTime)reader["last_update_utc"]).ToUniversalTime();
            return vessel;
        }

        private static string? NullableString(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            return string.IsNullOrEmpty(str) ? null : str;
        }

        #endregion
    }
}
