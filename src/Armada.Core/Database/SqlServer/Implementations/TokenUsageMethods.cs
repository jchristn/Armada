namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;

    /// <summary>
    /// SQL Server implementation of token-usage database operations.
    /// </summary>
    public class TokenUsageMethods : ITokenUsageMethods
    {
        #region Private-Members

        private readonly SqlServerDatabaseDriver _Driver;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TokenUsageMethods(SqlServerDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<TokenUsageRecord> CreateAsync(TokenUsageRecord record, CancellationToken token = default)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand cmd = conn.CreateCommand())
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

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string> { "id = @id" };
                List<SqlParameter> parameters = new List<SqlParameter> { new SqlParameter("@id", id) };
                ApplyQueryFilters(query, conditions, parameters);

                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT TOP 1 * FROM token_usage WHERE " + string.Join(" AND ", conditions) + ";";
                    foreach (SqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<SqlParameter> parameters = new List<SqlParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty;

                long totalCount;
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM token_usage" + whereClause + ";";
                    foreach (SqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                List<TokenUsageRecord> results = new List<TokenUsageRecord>();
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    int pageSize = query.PageSize <= 0 ? 25 : query.PageSize;
                    cmd.CommandText = "SELECT * FROM token_usage" + whereClause
                        + " ORDER BY created_utc DESC OFFSET @offset ROWS FETCH NEXT @page_size ROWS ONLY;";
                    foreach (SqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    cmd.Parameters.AddWithValue("@offset", query.Offset);
                    cmd.Parameters.AddWithValue("@page_size", pageSize);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<SqlParameter> parameters = new List<SqlParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty;

                List<TokenUsageRecord> results = new List<TokenUsageRecord>();
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM token_usage" + whereClause + " ORDER BY created_utc DESC;";
                    foreach (SqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<SqlParameter> parameters = new List<SqlParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty;

                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM token_usage" + whereClause + ";";
                    foreach (SqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        #endregion

        #region Private-Methods

        private static void BindRecord(SqlCommand cmd, TokenUsageRecord record)
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
            cmd.Parameters.AddWithValue("@estimated", record.Estimated);
            cmd.Parameters.AddWithValue("@created_utc", record.CreatedUtc);
        }

        private static TokenUsageRecord RecordFromReader(SqlDataReader reader)
        {
            return new TokenUsageRecord
            {
                Id = reader["id"].ToString()!,
                TenantId = SqlServerDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = SqlServerDatabaseDriver.NullableString(reader["user_id"]),
                Model = reader["model"].ToString() ?? string.Empty,
                Runtime = SqlServerDatabaseDriver.NullableString(reader["runtime"]),
                Source = reader["source"].ToString() ?? string.Empty,
                SourceId = SqlServerDatabaseDriver.NullableString(reader["source_id"]),
                VesselId = SqlServerDatabaseDriver.NullableString(reader["vessel_id"]),
                CaptainId = SqlServerDatabaseDriver.NullableString(reader["captain_id"]),
                HarborId = SqlServerDatabaseDriver.NullableString(reader["harbor_id"]),
                InputTokens = Convert.ToInt64(reader["input_tokens"]),
                OutputTokens = Convert.ToInt64(reader["output_tokens"]),
                CachedTokens = Convert.ToInt64(reader["cached_tokens"]),
                TotalTokens = Convert.ToInt64(reader["total_tokens"]),
                Estimated = Convert.ToBoolean(reader["estimated"]),
                CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc)
            };
        }

        private static SqlParameter CloneParameter(SqlParameter parameter)
        {
            return new SqlParameter(parameter.ParameterName, parameter.Value);
        }

        private static void ApplyQueryFilters(TokenUsageQuery? query, List<string> conditions, List<SqlParameter> parameters)
        {
            if (query == null) return;

            if (!string.IsNullOrWhiteSpace(query.TenantId))
            {
                conditions.Add("tenant_id = @tenant_id");
                parameters.Add(new SqlParameter("@tenant_id", query.TenantId));
            }
            if (!string.IsNullOrWhiteSpace(query.UserId))
            {
                conditions.Add("user_id = @user_id");
                parameters.Add(new SqlParameter("@user_id", query.UserId));
            }
            if (!string.IsNullOrWhiteSpace(query.Model))
            {
                conditions.Add("model = @model");
                parameters.Add(new SqlParameter("@model", query.Model));
            }
            if (!string.IsNullOrWhiteSpace(query.Runtime))
            {
                conditions.Add("runtime = @runtime");
                parameters.Add(new SqlParameter("@runtime", query.Runtime));
            }
            if (!string.IsNullOrWhiteSpace(query.Source))
            {
                conditions.Add("source = @source");
                parameters.Add(new SqlParameter("@source", query.Source));
            }
            if (!string.IsNullOrWhiteSpace(query.VesselId))
            {
                conditions.Add("vessel_id = @vessel_id");
                parameters.Add(new SqlParameter("@vessel_id", query.VesselId));
            }
            if (!string.IsNullOrWhiteSpace(query.CaptainId))
            {
                conditions.Add("captain_id = @captain_id");
                parameters.Add(new SqlParameter("@captain_id", query.CaptainId));
            }
            if (!string.IsNullOrWhiteSpace(query.HarborId))
            {
                conditions.Add("harbor_id = @harbor_id");
                parameters.Add(new SqlParameter("@harbor_id", query.HarborId));
            }
            if (query.FromUtc.HasValue)
            {
                conditions.Add("created_utc >= @from_utc");
                parameters.Add(new SqlParameter("@from_utc", query.FromUtc.Value));
            }
            if (query.ToUtc.HasValue)
            {
                conditions.Add("created_utc <= @to_utc");
                parameters.Add(new SqlParameter("@to_utc", query.ToUtc.Value));
            }
        }

        #endregion
    }
}
