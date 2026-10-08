namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of token-usage database operations.
    /// </summary>
    public class TokenUsageMethods : ITokenUsageMethods
    {
        #region Private-Members

        private readonly string _ConnectionString;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TokenUsageMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<TokenUsageRecord> CreateAsync(TokenUsageRecord record, CancellationToken token = default)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string> { "id = @id" };
                List<MySqlParameter> parameters = new List<MySqlParameter> { new MySqlParameter("@id", id) };
                ApplyQueryFilters(query, conditions, parameters);

                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM token_usage WHERE " + string.Join(" AND ", conditions) + " LIMIT 1;";
                    foreach (MySqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<MySqlParameter> parameters = new List<MySqlParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty;

                long totalCount;
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM token_usage" + whereClause + ";";
                    foreach (MySqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                List<TokenUsageRecord> results = new List<TokenUsageRecord>();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    int pageSize = query.PageSize <= 0 ? 25 : query.PageSize;
                    cmd.CommandText = "SELECT * FROM token_usage" + whereClause
                        + " ORDER BY created_utc DESC LIMIT " + pageSize + " OFFSET " + query.Offset + ";";
                    foreach (MySqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<MySqlParameter> parameters = new List<MySqlParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty;

                List<TokenUsageRecord> results = new List<TokenUsageRecord>();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM token_usage" + whereClause + " ORDER BY created_utc DESC;";
                    foreach (MySqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<MySqlParameter> parameters = new List<MySqlParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty;

                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM token_usage" + whereClause + ";";
                    foreach (MySqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        #endregion

        #region Private-Methods

        private static void BindRecord(MySqlCommand cmd, TokenUsageRecord record)
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
            cmd.Parameters.AddWithValue("@created_utc", MysqlDatabaseDriver.ToIso8601(record.CreatedUtc));
        }

        private static TokenUsageRecord RecordFromReader(MySqlDataReader reader)
        {
            return new TokenUsageRecord
            {
                Id = reader["id"].ToString()!,
                TenantId = MysqlDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = MysqlDatabaseDriver.NullableString(reader["user_id"]),
                Model = reader["model"].ToString() ?? string.Empty,
                Runtime = MysqlDatabaseDriver.NullableString(reader["runtime"]),
                Source = reader["source"].ToString() ?? string.Empty,
                SourceId = MysqlDatabaseDriver.NullableString(reader["source_id"]),
                VesselId = MysqlDatabaseDriver.NullableString(reader["vessel_id"]),
                CaptainId = MysqlDatabaseDriver.NullableString(reader["captain_id"]),
                HarborId = MysqlDatabaseDriver.NullableString(reader["harbor_id"]),
                InputTokens = Convert.ToInt64(reader["input_tokens"]),
                OutputTokens = Convert.ToInt64(reader["output_tokens"]),
                CachedTokens = Convert.ToInt64(reader["cached_tokens"]),
                TotalTokens = Convert.ToInt64(reader["total_tokens"]),
                Estimated = Convert.ToInt32(reader["estimated"]) == 1,
                CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc)
            };
        }

        private static MySqlParameter CloneParameter(MySqlParameter parameter)
        {
            return new MySqlParameter(parameter.ParameterName, parameter.Value);
        }

        private static void ApplyQueryFilters(TokenUsageQuery? query, List<string> conditions, List<MySqlParameter> parameters)
        {
            if (query == null) return;

            if (!string.IsNullOrWhiteSpace(query.TenantId))
            {
                conditions.Add("tenant_id = @tenant_id");
                parameters.Add(new MySqlParameter("@tenant_id", query.TenantId));
            }
            if (!string.IsNullOrWhiteSpace(query.UserId))
            {
                conditions.Add("user_id = @user_id");
                parameters.Add(new MySqlParameter("@user_id", query.UserId));
            }
            if (!string.IsNullOrWhiteSpace(query.Model))
            {
                conditions.Add("model = @model");
                parameters.Add(new MySqlParameter("@model", query.Model));
            }
            if (!string.IsNullOrWhiteSpace(query.Runtime))
            {
                conditions.Add("runtime = @runtime");
                parameters.Add(new MySqlParameter("@runtime", query.Runtime));
            }
            if (!string.IsNullOrWhiteSpace(query.Source))
            {
                conditions.Add("source = @source");
                parameters.Add(new MySqlParameter("@source", query.Source));
            }
            if (!string.IsNullOrWhiteSpace(query.VesselId))
            {
                conditions.Add("vessel_id = @vessel_id");
                parameters.Add(new MySqlParameter("@vessel_id", query.VesselId));
            }
            if (!string.IsNullOrWhiteSpace(query.CaptainId))
            {
                conditions.Add("captain_id = @captain_id");
                parameters.Add(new MySqlParameter("@captain_id", query.CaptainId));
            }
            if (!string.IsNullOrWhiteSpace(query.HarborId))
            {
                conditions.Add("harbor_id = @harbor_id");
                parameters.Add(new MySqlParameter("@harbor_id", query.HarborId));
            }
            if (query.FromUtc.HasValue)
            {
                conditions.Add("created_utc >= @from_utc");
                parameters.Add(new MySqlParameter("@from_utc", MysqlDatabaseDriver.ToIso8601(query.FromUtc.Value)));
            }
            if (query.ToUtc.HasValue)
            {
                conditions.Add("created_utc <= @to_utc");
                parameters.Add(new MySqlParameter("@to_utc", MysqlDatabaseDriver.ToIso8601(query.ToUtc.Value)));
            }
        }

        #endregion
    }
}
