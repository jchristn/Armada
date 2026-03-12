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
    /// PostgreSQL implementation of signal database operations.
    /// </summary>
    public class SignalMethods : ISignalMethods
    {
        #region Private-Members

        private NpgsqlDataSource _DataSource;
        private static readonly string _Iso8601Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the PostgreSQL signal methods.
        /// </summary>
        /// <param name="dataSource">NpgsqlDataSource instance.</param>
        public SignalMethods(NpgsqlDataSource dataSource)
        {
            _DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a signal.
        /// </summary>
        /// <param name="signal">Signal to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created signal.</returns>
        public async Task<Signal> CreateAsync(Signal signal, CancellationToken token = default)
        {
            if (signal == null) throw new ArgumentNullException(nameof(signal));

            using (NpgsqlConnection conn = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            {
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = @"INSERT INTO signals (id, from_captain_id, to_captain_id, type, payload, read, created_utc)
                        VALUES (@id, @from_captain_id, @to_captain_id, @type, @payload, @read, @created_utc);";
                    cmd.Parameters.AddWithValue("@id", signal.Id);
                    cmd.Parameters.AddWithValue("@from_captain_id", (object?)signal.FromCaptainId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@to_captain_id", (object?)signal.ToCaptainId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@type", signal.Type.ToString());
                    cmd.Parameters.AddWithValue("@payload", (object?)signal.Payload ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@read", signal.Read);
                    cmd.Parameters.AddWithValue("@created_utc", ToIso8601(signal.CreatedUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return signal;
        }

        /// <summary>
        /// Read a signal by identifier.
        /// </summary>
        /// <param name="id">Signal identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Signal if found, null otherwise.</returns>
        public async Task<Signal?> ReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (NpgsqlConnection conn = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            {
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM signals WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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
        /// <param name="captainId">Recipient captain identifier.</param>
        /// <param name="unreadOnly">If true, only return unread signals.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of signals for the recipient.</returns>
        public async Task<List<Signal>> EnumerateByRecipientAsync(string captainId, bool unreadOnly = true, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            List<Signal> results = new List<Signal>();

            using (NpgsqlConnection conn = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            {
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = unreadOnly
                        ? "SELECT * FROM signals WHERE to_captain_id = @to_captain_id AND read = FALSE ORDER BY created_utc DESC;"
                        : "SELECT * FROM signals WHERE to_captain_id = @to_captain_id ORDER BY created_utc DESC;";
                    cmd.Parameters.AddWithValue("@to_captain_id", captainId);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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
        /// <param name="count">Maximum number of signals to return.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of recent signals.</returns>
        public async Task<List<Signal>> EnumerateRecentAsync(int count = 50, CancellationToken token = default)
        {
            List<Signal> results = new List<Signal>();

            using (NpgsqlConnection conn = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            {
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM signals ORDER BY created_utc DESC LIMIT @count;";
                    cmd.Parameters.AddWithValue("@count", count);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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
        /// <param name="id">Signal identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task MarkReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (NpgsqlConnection conn = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            {
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "UPDATE signals SET read = TRUE WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Enumerate signals with pagination and filtering.
        /// </summary>
        /// <param name="query">Enumeration query with pagination and filter parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated enumeration result of signals.</returns>
        public async Task<EnumerationResult<Signal>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
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
                if (!string.IsNullOrEmpty(query.ToCaptainId))
                {
                    conditions.Add("to_captain_id = @to_captain_id");
                    parameters.Add(new NpgsqlParameter("@to_captain_id", query.ToCaptainId));
                }
                if (!string.IsNullOrEmpty(query.SignalType))
                {
                    conditions.Add("type = @type");
                    parameters.Add(new NpgsqlParameter("@type", query.SignalType));
                }
                if (query.UnreadOnly.HasValue && query.UnreadOnly.Value)
                {
                    conditions.Add("read = FALSE");
                }

                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                // Count
                long totalCount = 0;
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT COUNT(*) FROM signals" + whereClause + ";";
                    foreach (NpgsqlParameter p in parameters) cmd.Parameters.Add(new NpgsqlParameter(p.ParameterName, p.NpgsqlDbType) { Value = p.Value });
                    totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                }

                // Query
                List<Signal> results = new List<Signal>();
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM signals" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (NpgsqlParameter p in parameters) cmd.Parameters.Add(new NpgsqlParameter(p.ParameterName, p.NpgsqlDbType) { Value = p.Value });
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

        private static Signal SignalFromReader(NpgsqlDataReader reader)
        {
            Signal signal = new Signal();
            signal.Id = reader["id"].ToString()!;
            signal.FromCaptainId = NullableString(reader["from_captain_id"]);
            signal.ToCaptainId = NullableString(reader["to_captain_id"]);
            signal.Type = Enum.Parse<SignalTypeEnum>(reader["type"].ToString()!);
            signal.Payload = NullableString(reader["payload"]);
            signal.Read = Convert.ToBoolean(reader["read"]);
            signal.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            return signal;
        }

        #endregion
    }
}
