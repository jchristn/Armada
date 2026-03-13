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
    /// SQL Server implementation of signal database operations.
    /// </summary>
    internal class SignalMethods : ISignalMethods
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
        internal SignalMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a signal.
        /// </summary>
        public async Task<Signal> CreateAsync(Signal signal, CancellationToken token = default)
        {
            if (signal == null) throw new ArgumentNullException(nameof(signal));

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO signals (id, from_captain_id, to_captain_id, type, payload, [read], created_utc)
                        VALUES (@id, @from_captain_id, @to_captain_id, @type, @payload, @read, @created_utc);";
                    cmd.Parameters.AddWithValue("@id", signal.Id);
                    cmd.Parameters.AddWithValue("@from_captain_id", (object?)signal.FromCaptainId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@to_captain_id", (object?)signal.ToCaptainId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@type", signal.Type.ToString());
                    cmd.Parameters.AddWithValue("@payload", (object?)signal.Payload ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@read", signal.Read ? 1 : 0);
                    cmd.Parameters.AddWithValue("@created_utc", ToIso8601(signal.CreatedUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return signal;
        }

        /// <summary>
        /// Read a signal by identifier.
        /// </summary>
        public async Task<Signal?> ReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM signals WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return SignalFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Enumerate signals by recipient captain identifier.
        /// </summary>
        public async Task<List<Signal>> EnumerateByRecipientAsync(string captainId, bool unreadOnly = true, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            List<Signal> results = new List<Signal>();

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = unreadOnly
                        ? "SELECT * FROM signals WHERE to_captain_id = @to_captain_id AND [read] = 0 ORDER BY created_utc DESC;"
                        : "SELECT * FROM signals WHERE to_captain_id = @to_captain_id ORDER BY created_utc DESC;";
                    cmd.Parameters.AddWithValue("@to_captain_id", captainId);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(SignalFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Enumerate recent signals.
        /// </summary>
        public async Task<List<Signal>> EnumerateRecentAsync(int count = 50, CancellationToken token = default)
        {
            List<Signal> results = new List<Signal>();

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT TOP (@count) * FROM signals ORDER BY created_utc DESC;";
                    cmd.Parameters.AddWithValue("@count", count);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(SignalFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Mark a signal as read.
        /// </summary>
        public async Task MarkReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE signals SET [read] = 1 WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Enumerate signals with pagination and filtering.
        /// </summary>
        public async Task<EnumerationResult<Signal>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
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
                if (!string.IsNullOrEmpty(query.ToCaptainId))
                {
                    conditions.Add("to_captain_id = @to_captain_id");
                    parameters.Add(new SqlParameter("@to_captain_id", query.ToCaptainId));
                }
                if (!string.IsNullOrEmpty(query.SignalType))
                {
                    conditions.Add("type = @type");
                    parameters.Add(new SqlParameter("@type", query.SignalType));
                }
                if (query.UnreadOnly.HasValue && query.UnreadOnly.Value)
                {
                    conditions.Add("[read] = 0");
                }

                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                // Count
                int totalCount = 0;
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM signals" + whereClause + ";";
                    foreach (SqlParameter p in parameters) cmd.Parameters.Add(new SqlParameter(p.ParameterName, p.Value));
                    totalCount = (int)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                }

                // Query
                List<Signal> results = new List<Signal>();
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM signals" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " OFFSET " + query.Offset + " ROWS FETCH NEXT " + query.PageSize + " ROWS ONLY;";
                    foreach (SqlParameter p in parameters) cmd.Parameters.Add(new SqlParameter(p.ParameterName, p.Value));
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(SignalFromReader(reader));
                    }
                }

                return EnumerationResult<Signal>.Create(query, results, totalCount);
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

        private static string? NullableString(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            return string.IsNullOrEmpty(str) ? null : str;
        }

        private static Signal SignalFromReader(SqlDataReader reader)
        {
            Signal signal = new Signal();
            signal.Id = reader["id"].ToString()!;
            signal.FromCaptainId = NullableString(reader["from_captain_id"]);
            signal.ToCaptainId = NullableString(reader["to_captain_id"]);
            signal.Type = Enum.Parse<SignalTypeEnum>(reader["type"].ToString()!);
            signal.Payload = NullableString(reader["payload"]);
            signal.Read = Convert.ToInt32(reader["read"]) == 1;
            signal.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            return signal;
        }

        #endregion
    }
}
