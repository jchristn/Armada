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
    /// PostgreSQL implementation of dock database operations.
    /// </summary>
    public class DockMethods : IDockMethods
    {
        #region Private-Members

        private string _Header = "[DockMethods] ";
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
        public DockMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a dock.
        /// </summary>
        /// <param name="dock">Dock to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created dock.</returns>
        public async Task<Dock> CreateAsync(Dock dock, CancellationToken token = default)
        {
            if (dock == null) throw new ArgumentNullException(nameof(dock));
            dock.LastUpdateUtc = DateTime.UtcNow;

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = @"INSERT INTO docks (id, vessel_id, captain_id, worktree_path, branch_name, active, created_utc, last_update_utc)
                        VALUES (@id, @vessel_id, @captain_id, @worktree_path, @branch_name, @active, @created_utc, @last_update_utc);";
                    cmd.Parameters.AddWithValue("@id", dock.Id);
                    cmd.Parameters.AddWithValue("@vessel_id", dock.VesselId);
                    cmd.Parameters.AddWithValue("@captain_id", (object?)dock.CaptainId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@worktree_path", (object?)dock.WorktreePath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@branch_name", (object?)dock.BranchName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@active", dock.Active);
                    cmd.Parameters.AddWithValue("@created_utc", dock.CreatedUtc);
                    cmd.Parameters.AddWithValue("@last_update_utc", dock.LastUpdateUtc);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return dock;
        }

        /// <summary>
        /// Read a dock by identifier.
        /// </summary>
        /// <param name="id">Dock identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Dock or null if not found.</returns>
        public async Task<Dock?> ReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM docks WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return DockFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Update a dock.
        /// </summary>
        /// <param name="dock">Dock to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated dock.</returns>
        public async Task<Dock> UpdateAsync(Dock dock, CancellationToken token = default)
        {
            if (dock == null) throw new ArgumentNullException(nameof(dock));
            dock.LastUpdateUtc = DateTime.UtcNow;

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = @"UPDATE docks SET
                        vessel_id = @vessel_id,
                        captain_id = @captain_id,
                        worktree_path = @worktree_path,
                        branch_name = @branch_name,
                        active = @active,
                        last_update_utc = @last_update_utc
                        WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", dock.Id);
                    cmd.Parameters.AddWithValue("@vessel_id", dock.VesselId);
                    cmd.Parameters.AddWithValue("@captain_id", (object?)dock.CaptainId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@worktree_path", (object?)dock.WorktreePath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@branch_name", (object?)dock.BranchName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@active", dock.Active);
                    cmd.Parameters.AddWithValue("@last_update_utc", dock.LastUpdateUtc);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return dock;
        }

        /// <summary>
        /// Delete a dock by identifier.
        /// </summary>
        /// <param name="id">Dock identifier.</param>
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
                    cmd.CommandText = "DELETE FROM docks WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Enumerate all docks.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of all docks.</returns>
        public async Task<List<Dock>> EnumerateAsync(CancellationToken token = default)
        {
            List<Dock> results = new List<Dock>();

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM docks ORDER BY created_utc DESC;";
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(DockFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Enumerate docks with pagination and filtering.
        /// </summary>
        /// <param name="query">Enumeration query parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated enumeration result.</returns>
        public async Task<EnumerationResult<Dock>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
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

                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                // Count
                long totalCount = 0;
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT COUNT(*) FROM docks" + whereClause + ";";
                    foreach (NpgsqlParameter p in parameters) cmd.Parameters.Add(new NpgsqlParameter(p.ParameterName, p.Value));
                    totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                }

                // Query
                List<Dock> results = new List<Dock>();
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM docks" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (NpgsqlParameter p in parameters) cmd.Parameters.Add(new NpgsqlParameter(p.ParameterName, p.Value));
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(DockFromReader(reader));
                    }
                }

                return EnumerationResult<Dock>.Create(query, results, totalCount);
            }
        }

        /// <summary>
        /// Enumerate docks by vessel identifier.
        /// </summary>
        /// <param name="vesselId">Vessel identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of docks for the vessel.</returns>
        public async Task<List<Dock>> EnumerateByVesselAsync(string vesselId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            List<Dock> results = new List<Dock>();

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM docks WHERE vessel_id = @vessel_id ORDER BY created_utc DESC;";
                    cmd.Parameters.AddWithValue("@vessel_id", vesselId);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(DockFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Find an available dock for a vessel (no captain assigned, active).
        /// </summary>
        /// <param name="vesselId">Vessel identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Available dock or null if none found.</returns>
        public async Task<Dock?> FindAvailableAsync(string vesselId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM docks WHERE vessel_id = @vesselId AND captain_id IS NULL AND active = TRUE LIMIT 1;";
                    cmd.Parameters.AddWithValue("@vesselId", vesselId);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return DockFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Check if a dock exists by identifier.
        /// </summary>
        /// <param name="id">Dock identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the dock exists.</returns>
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT COUNT(*) FROM docks WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                    return count > 0;
                }
            }
        }

        #endregion

        #region Private-Methods

        private static Dock DockFromReader(NpgsqlDataReader reader)
        {
            Dock dock = new Dock();
            dock.Id = reader["id"].ToString()!;
            dock.VesselId = reader["vessel_id"].ToString()!;
            dock.CaptainId = NullableString(reader["captain_id"]);
            dock.WorktreePath = NullableString(reader["worktree_path"]);
            dock.BranchName = NullableString(reader["branch_name"]);
            dock.Active = (bool)reader["active"];
            dock.CreatedUtc = ((DateTime)reader["created_utc"]).ToUniversalTime();
            dock.LastUpdateUtc = ((DateTime)reader["last_update_utc"]).ToUniversalTime();
            return dock;
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
