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
    /// SQL Server implementation of dock database operations.
    /// </summary>
    internal class DockMethods : IDockMethods
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
        internal DockMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a dock.
        /// </summary>
        public async Task<Dock> CreateAsync(Dock dock, CancellationToken token = default)
        {
            if (dock == null) throw new ArgumentNullException(nameof(dock));
            dock.LastUpdateUtc = DateTime.UtcNow;

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO docks (id, vessel_id, captain_id, worktree_path, branch_name, active, created_utc, last_update_utc)
                        VALUES (@id, @vessel_id, @captain_id, @worktree_path, @branch_name, @active, @created_utc, @last_update_utc);";
                    cmd.Parameters.AddWithValue("@id", dock.Id);
                    cmd.Parameters.AddWithValue("@vessel_id", dock.VesselId);
                    cmd.Parameters.AddWithValue("@captain_id", (object?)dock.CaptainId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@worktree_path", (object?)dock.WorktreePath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@branch_name", (object?)dock.BranchName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@active", dock.Active ? 1 : 0);
                    cmd.Parameters.AddWithValue("@created_utc", ToIso8601(dock.CreatedUtc));
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(dock.LastUpdateUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return dock;
        }

        /// <summary>
        /// Read a dock by identifier.
        /// </summary>
        public async Task<Dock?> ReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM docks WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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
        public async Task<Dock> UpdateAsync(Dock dock, CancellationToken token = default)
        {
            if (dock == null) throw new ArgumentNullException(nameof(dock));
            dock.LastUpdateUtc = DateTime.UtcNow;

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
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
                    cmd.Parameters.AddWithValue("@active", dock.Active ? 1 : 0);
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(dock.LastUpdateUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return dock;
        }

        /// <summary>
        /// Delete a dock by identifier.
        /// </summary>
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM docks WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Enumerate all docks.
        /// </summary>
        public async Task<List<Dock>> EnumerateAsync(CancellationToken token = default)
        {
            List<Dock> results = new List<Dock>();

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM docks ORDER BY created_utc;";
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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
        public async Task<EnumerationResult<Dock>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
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
                    cmd.CommandText = "SELECT COUNT(*) FROM docks" + whereClause + ";";
                    foreach (SqlParameter p in parameters) cmd.Parameters.Add(new SqlParameter(p.ParameterName, p.Value));
                    totalCount = (int)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                }

                // Query
                List<Dock> results = new List<Dock>();
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM docks" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " OFFSET " + query.Offset + " ROWS FETCH NEXT " + query.PageSize + " ROWS ONLY;";
                    foreach (SqlParameter p in parameters) cmd.Parameters.Add(new SqlParameter(p.ParameterName, p.Value));
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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
        public async Task<List<Dock>> EnumerateByVesselAsync(string vesselId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            List<Dock> results = new List<Dock>();

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM docks WHERE vessel_id = @vessel_id ORDER BY created_utc;";
                    cmd.Parameters.AddWithValue("@vessel_id", vesselId);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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
        public async Task<Dock?> FindAvailableAsync(string vesselId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT TOP 1 * FROM docks WHERE vessel_id = @vessel_id AND active = 1 AND captain_id IS NULL;";
                    cmd.Parameters.AddWithValue("@vessel_id", vesselId);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM docks WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    int count = (int)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                    return count > 0;
                }
            }
        }

        #endregion

        #region Private-Methods

        private static Dock DockFromReader(SqlDataReader reader)
        {
            Dock dock = new Dock();
            dock.Id = reader["id"].ToString()!;
            dock.VesselId = reader["vessel_id"].ToString()!;
            dock.CaptainId = NullableString(reader["captain_id"]);
            dock.WorktreePath = NullableString(reader["worktree_path"]);
            dock.BranchName = NullableString(reader["branch_name"]);
            dock.Active = Convert.ToInt32(reader["active"]) == 1;
            dock.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            dock.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
            return dock;
        }

        private static string ToIso8601(DateTime dt)
        {
            return dt.ToUniversalTime().ToString(_Iso8601Format, CultureInfo.InvariantCulture);
        }

        private static DateTime FromIso8601(string value)
        {
            return DateTime.Parse(value, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
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

        #endregion
    }
}
