namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using SyslogLogging;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// SQLite implementation of token-usage database operations.
    /// </summary>
    public class TokenUsageMethods : ITokenUsageMethods
    {
        #region Private-Members

#pragma warning disable CS0414
        private readonly string _Header = "[TokenUsageMethods] ";
#pragma warning restore CS0414
        private readonly SqliteDatabaseDriver _Driver;
        private readonly DatabaseSettings _Settings;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TokenUsageMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<TokenUsageRecord> CreateAsync(TokenUsageRecord record, CancellationToken token = default)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO token_usage (
                            id, tenant_id, user_id, model, runtime, source, source_id, vessel_id, captain_id, harbor_id,
                            input_tokens, output_tokens, cached_tokens, total_tokens, estimated, created_utc
                        ) VALUES (
                            @id, @tenant_id, @user_id, @model, @runtime, @source, @source_id, @vessel_id, @captain_id, @harbor_id,
                            @input_tokens, @output_tokens, @cached_tokens, @total_tokens, @estimated, @created_utc
                        );";
                    BindRecord(cmd, record);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return record;
        }

        /// <inheritdoc />
        public async Task<TokenUsageRecord?> ReadAsync(string id, TokenUsageQuery? query = null, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string> { "id = @id" };
                List<SqliteParameter> parameters = new List<SqliteParameter> { new SqliteParameter("@id", id) };
                ApplyQueryFilters(query, conditions, parameters);

                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM token_usage WHERE " + string.Join(" AND ", conditions) + " LIMIT 1;";
                    foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return RecordFromReader(reader);
                    }
                }

                return null;
            }
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<TokenUsageRecord>> EnumerateAsync(TokenUsageQuery query, CancellationToken token = default)
        {
            query ??= new TokenUsageQuery();

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<SqliteParameter> parameters = new List<SqliteParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty;

                long totalCount;
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM token_usage" + whereClause + ";";
                    foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                List<TokenUsageRecord> results = new List<TokenUsageRecord>();
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    int pageSize = query.PageSize <= 0 ? 25 : query.PageSize;
                    int offset = query.PageNumber <= 1 ? 0 : (query.PageNumber - 1) * pageSize;
                    cmd.CommandText = "SELECT * FROM token_usage" + whereClause
                        + " ORDER BY created_utc DESC LIMIT " + pageSize + " OFFSET " + offset + ";";
                    foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(RecordFromReader(reader));
                    }
                }

                return EnumerationResult<TokenUsageRecord>.Create(
                    new EnumerationQuery { PageNumber = query.PageNumber, PageSize = query.PageSize <= 0 ? 25 : query.PageSize },
                    results,
                    totalCount);
            }
        }

        /// <inheritdoc />
        public async Task<List<TokenUsageRecord>> EnumerateForSummaryAsync(TokenUsageQuery query, CancellationToken token = default)
        {
            query ??= new TokenUsageQuery();

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<SqliteParameter> parameters = new List<SqliteParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty;

                List<TokenUsageRecord> results = new List<TokenUsageRecord>();
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM token_usage" + whereClause + " ORDER BY created_utc DESC;";
                    foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(RecordFromReader(reader));
                    }
                }

                return results;
            }
        }

        /// <inheritdoc />
        public async Task<int> DeleteByFilterAsync(TokenUsageQuery query, CancellationToken token = default)
        {
            query ??= new TokenUsageQuery();

            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<SqliteParameter> parameters = new List<SqliteParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty;

                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM token_usage" + whereClause + ";";
                    foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        #endregion

        #region Private-Methods

        private static void BindRecord(SqliteCommand cmd, TokenUsageRecord record)
        {
            cmd.Parameters.AddWithValue("@id", record.Id);
            cmd.Parameters.AddWithValue("@tenant_id", (object?)record.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_id", (object?)record.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@model", record.Model ?? string.Empty);
            cmd.Parameters.AddWithValue("@runtime", (object?)record.Runtime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@source", record.Source ?? string.Empty);
            cmd.Parameters.AddWithValue("@source_id", (object?)record.SourceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vessel_id", (object?)record.VesselId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@captain_id", (object?)record.CaptainId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@harbor_id", (object?)record.HarborId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@input_tokens", record.InputTokens);
            cmd.Parameters.AddWithValue("@output_tokens", record.OutputTokens);
            cmd.Parameters.AddWithValue("@cached_tokens", record.CachedTokens);
            cmd.Parameters.AddWithValue("@total_tokens", record.TotalTokens);
            cmd.Parameters.AddWithValue("@estimated", record.Estimated ? 1 : 0);
            cmd.Parameters.AddWithValue("@created_utc", SqliteDatabaseDriver.ToIso8601(record.CreatedUtc));
        }

        private static TokenUsageRecord RecordFromReader(SqliteDataReader reader)
        {
            return new TokenUsageRecord
            {
                Id = reader["id"].ToString()!,
                TenantId = NullableString(reader["tenant_id"]),
                UserId = NullableString(reader["user_id"]),
                Model = reader["model"].ToString() ?? string.Empty,
                Runtime = NullableString(reader["runtime"]),
                Source = reader["source"].ToString() ?? string.Empty,
                SourceId = NullableString(reader["source_id"]),
                VesselId = NullableString(reader["vessel_id"]),
                CaptainId = NullableString(reader["captain_id"]),
                HarborId = NullableString(reader["harbor_id"]),
                InputTokens = Convert.ToInt64(reader["input_tokens"]),
                OutputTokens = Convert.ToInt64(reader["output_tokens"]),
                CachedTokens = Convert.ToInt64(reader["cached_tokens"]),
                TotalTokens = Convert.ToInt64(reader["total_tokens"]),
                Estimated = Convert.ToInt32(reader["estimated"]) == 1,
                CreatedUtc = SqliteDatabaseDriver.FromIso8601(reader["created_utc"].ToString()!)
            };
        }

        private static string? NullableString(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            return string.IsNullOrEmpty(str) ? null : str;
        }

        private static SqliteParameter CloneParameter(SqliteParameter parameter)
        {
            return new SqliteParameter(parameter.ParameterName, parameter.Value);
        }

        private static void ApplyQueryFilters(TokenUsageQuery? query, List<string> conditions, List<SqliteParameter> parameters)
        {
            if (query == null) return;

            if (!string.IsNullOrWhiteSpace(query.TenantId))
            {
                conditions.Add("tenant_id = @tenant_id");
                parameters.Add(new SqliteParameter("@tenant_id", query.TenantId));
            }
            if (!string.IsNullOrWhiteSpace(query.UserId))
            {
                conditions.Add("user_id = @user_id");
                parameters.Add(new SqliteParameter("@user_id", query.UserId));
            }
            if (!string.IsNullOrWhiteSpace(query.Model))
            {
                conditions.Add("model = @model");
                parameters.Add(new SqliteParameter("@model", query.Model));
            }
            if (!string.IsNullOrWhiteSpace(query.Runtime))
            {
                conditions.Add("runtime = @runtime");
                parameters.Add(new SqliteParameter("@runtime", query.Runtime));
            }
            if (!string.IsNullOrWhiteSpace(query.Source))
            {
                conditions.Add("source = @source");
                parameters.Add(new SqliteParameter("@source", query.Source));
            }
            if (!string.IsNullOrWhiteSpace(query.VesselId))
            {
                conditions.Add("vessel_id = @vessel_id");
                parameters.Add(new SqliteParameter("@vessel_id", query.VesselId));
            }
            if (!string.IsNullOrWhiteSpace(query.CaptainId))
            {
                conditions.Add("captain_id = @captain_id");
                parameters.Add(new SqliteParameter("@captain_id", query.CaptainId));
            }
            if (!string.IsNullOrWhiteSpace(query.HarborId))
            {
                conditions.Add("harbor_id = @harbor_id");
                parameters.Add(new SqliteParameter("@harbor_id", query.HarborId));
            }
            if (query.FromUtc.HasValue)
            {
                conditions.Add("created_utc >= @from_utc");
                parameters.Add(new SqliteParameter("@from_utc", SqliteDatabaseDriver.ToIso8601(query.FromUtc.Value)));
            }
            if (query.ToUtc.HasValue)
            {
                conditions.Add("created_utc <= @to_utc");
                parameters.Add(new SqliteParameter("@to_utc", SqliteDatabaseDriver.ToIso8601(query.ToUtc.Value)));
            }
        }

        #endregion
    }
}
