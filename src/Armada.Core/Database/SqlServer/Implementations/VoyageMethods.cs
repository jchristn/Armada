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
    /// SQL Server implementation of voyage database operations.
    /// </summary>
    internal class VoyageMethods : IVoyageMethods
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
        internal VoyageMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a voyage.
        /// </summary>
        public async Task<Voyage> CreateAsync(Voyage voyage, CancellationToken token = default)
        {
            if (voyage == null) throw new ArgumentNullException(nameof(voyage));
            voyage.LastUpdateUtc = DateTime.UtcNow;

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO voyages (id, title, description, status, created_utc, completed_utc, last_update_utc, auto_push, auto_create_pull_requests, auto_merge_pull_requests)
                        VALUES (@id, @title, @description, @status, @created_utc, @completed_utc, @last_update_utc, @auto_push, @auto_create_pull_requests, @auto_merge_pull_requests);";
                    cmd.Parameters.AddWithValue("@id", voyage.Id);
                    cmd.Parameters.AddWithValue("@title", voyage.Title);
                    cmd.Parameters.AddWithValue("@description", (object?)voyage.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@status", voyage.Status.ToString());
                    cmd.Parameters.AddWithValue("@created_utc", ToIso8601(voyage.CreatedUtc));
                    cmd.Parameters.AddWithValue("@completed_utc", voyage.CompletedUtc.HasValue ? (object)ToIso8601(voyage.CompletedUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(voyage.LastUpdateUtc));
                    cmd.Parameters.AddWithValue("@auto_push", voyage.AutoPush.HasValue ? (object)(voyage.AutoPush.Value ? 1 : 0) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@auto_create_pull_requests", voyage.AutoCreatePullRequests.HasValue ? (object)(voyage.AutoCreatePullRequests.Value ? 1 : 0) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@auto_merge_pull_requests", voyage.AutoMergePullRequests.HasValue ? (object)(voyage.AutoMergePullRequests.Value ? 1 : 0) : DBNull.Value);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return voyage;
        }

        /// <summary>
        /// Read a voyage by identifier.
        /// </summary>
        public async Task<Voyage?> ReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM voyages WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return VoyageFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Update a voyage.
        /// </summary>
        public async Task<Voyage> UpdateAsync(Voyage voyage, CancellationToken token = default)
        {
            if (voyage == null) throw new ArgumentNullException(nameof(voyage));
            voyage.LastUpdateUtc = DateTime.UtcNow;

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE voyages SET
                        title = @title,
                        description = @description,
                        status = @status,
                        completed_utc = @completed_utc,
                        last_update_utc = @last_update_utc,
                        auto_push = @auto_push,
                        auto_create_pull_requests = @auto_create_pull_requests,
                        auto_merge_pull_requests = @auto_merge_pull_requests
                        WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", voyage.Id);
                    cmd.Parameters.AddWithValue("@title", voyage.Title);
                    cmd.Parameters.AddWithValue("@description", (object?)voyage.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@status", voyage.Status.ToString());
                    cmd.Parameters.AddWithValue("@completed_utc", voyage.CompletedUtc.HasValue ? (object)ToIso8601(voyage.CompletedUtc.Value) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(voyage.LastUpdateUtc));
                    cmd.Parameters.AddWithValue("@auto_push", voyage.AutoPush.HasValue ? (object)(voyage.AutoPush.Value ? 1 : 0) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@auto_create_pull_requests", voyage.AutoCreatePullRequests.HasValue ? (object)(voyage.AutoCreatePullRequests.Value ? 1 : 0) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@auto_merge_pull_requests", voyage.AutoMergePullRequests.HasValue ? (object)(voyage.AutoMergePullRequests.Value ? 1 : 0) : DBNull.Value);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return voyage;
        }

        /// <summary>
        /// Delete a voyage by identifier.
        /// </summary>
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM voyages WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Enumerate all voyages.
        /// </summary>
        public async Task<List<Voyage>> EnumerateAsync(CancellationToken token = default)
        {
            List<Voyage> results = new List<Voyage>();

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM voyages ORDER BY created_utc DESC;";
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(VoyageFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Enumerate voyages with pagination and filtering.
        /// </summary>
        public async Task<EnumerationResult<Voyage>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
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

                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                // Count
                int totalCount = 0;
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM voyages" + whereClause + ";";
                    foreach (SqlParameter p in parameters) cmd.Parameters.Add(new SqlParameter(p.ParameterName, p.Value));
                    totalCount = (int)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                }

                // Query
                List<Voyage> results = new List<Voyage>();
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM voyages" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " OFFSET " + query.Offset + " ROWS FETCH NEXT " + query.PageSize + " ROWS ONLY;";
                    foreach (SqlParameter p in parameters) cmd.Parameters.Add(new SqlParameter(p.ParameterName, p.Value));
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(VoyageFromReader(reader));
                    }
                }

                return EnumerationResult<Voyage>.Create(query, results, totalCount);
            }
        }

        /// <summary>
        /// Enumerate voyages by status.
        /// </summary>
        public async Task<List<Voyage>> EnumerateByStatusAsync(VoyageStatusEnum status, CancellationToken token = default)
        {
            List<Voyage> results = new List<Voyage>();

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM voyages WHERE status = @status ORDER BY created_utc DESC;";
                    cmd.Parameters.AddWithValue("@status", status.ToString());
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(VoyageFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Check if a voyage exists by identifier.
        /// </summary>
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM voyages WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    int count = (int)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                    return count > 0;
                }
            }
        }

        #endregion

        #region Private-Methods

        private static Voyage VoyageFromReader(SqlDataReader reader)
        {
            Voyage voyage = new Voyage();
            voyage.Id = reader["id"].ToString()!;
            voyage.Title = reader["title"].ToString()!;
            voyage.Description = NullableString(reader["description"]);
            voyage.Status = Enum.Parse<VoyageStatusEnum>(reader["status"].ToString()!);
            voyage.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            voyage.CompletedUtc = FromIso8601Nullable(reader["completed_utc"]);
            voyage.LastUpdateUtc = FromIso8601(reader["last_update_utc"].ToString()!);
            voyage.AutoPush = NullableBool(reader, "auto_push");
            voyage.AutoCreatePullRequests = NullableBool(reader, "auto_create_pull_requests");
            voyage.AutoMergePullRequests = NullableBool(reader, "auto_merge_pull_requests");
            return voyage;
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

        private static bool? NullableBool(SqlDataReader reader, string column)
        {
            try
            {
                object value = reader[column];
                if (value == null || value == DBNull.Value) return null;
                return Convert.ToInt32(value) == 1;
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}
