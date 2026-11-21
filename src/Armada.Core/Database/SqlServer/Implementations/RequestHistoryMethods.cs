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
    /// SQL Server implementation of request-history database operations.
    /// </summary>
    public class RequestHistoryMethods : IRequestHistoryMethods
    {
        private readonly SqlServerDatabaseDriver _Driver;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RequestHistoryMethods(SqlServerDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc />
        public async Task<RequestHistoryRecord> CreateAsync(RequestHistoryEntry entry, RequestHistoryDetail? detail, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlTransaction tx = (SqlTransaction)await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                {
                    using (SqlCommand cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"INSERT INTO request_history (
                                id, tenant_id, user_id, credential_id, principal_display, auth_method, method, route, route_template,
                                query_string, status_code, duration_ms, request_size_bytes, response_size_bytes, request_content_type,
                                response_content_type, is_success, client_ip, correlation_id, created_utc
                            ) VALUES (
                                @id, @tenant_id, @user_id, @credential_id, @principal_display, @auth_method, @method, @route, @route_template,
                                @query_string, @status_code, @duration_ms, @request_size_bytes, @response_size_bytes, @request_content_type,
                                @response_content_type, @is_success, @client_ip, @correlation_id, @created_utc
                            );";
                        BindEntry(cmd, entry);
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }

                    if (detail != null)
                    {
                        using (SqlCommand cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = @"INSERT INTO request_history_detail (
                                    request_history_id, path_params_json, query_params_json, request_headers_json, response_headers_json,
                                    request_body_text, response_body_text, request_body_truncated, response_body_truncated
                                ) VALUES (
                                    @request_history_id, @path_params_json, @query_params_json, @request_headers_json, @response_headers_json,
                                    @request_body_text, @response_body_text, @request_body_truncated, @response_body_truncated
                                );";
                            BindDetail(cmd, detail);
                            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                        }
                    }

                    await tx.CommitAsync(token).ConfigureAwait(false);
                }
            }

            return new RequestHistoryRecord { Entry = entry, Detail = detail };
        }

        /// <inheritdoc />
        public async Task<RequestHistoryRecord?> ReadAsync(string id, RequestHistoryQuery? query = null, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string> { "id = @id" };
                List<SqlParameter> parameters = new List<SqlParameter> { new SqlParameter("@id", id) };
                ApplyQueryFilters(query, conditions, parameters);

                RequestHistoryEntry? entry = null;
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT TOP 1 * FROM request_history WHERE " + string.Join(" AND ", conditions) + ";";
                    foreach (SqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            entry = EntryFromReader(reader);
                    }
                }

                if (entry == null) return null;

                RequestHistoryDetail? detail = null;
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT TOP 1 * FROM request_history_detail WHERE request_history_id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            detail = DetailFromReader(reader);
                    }
                }

                return new RequestHistoryRecord { Entry = entry, Detail = detail };
            }
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<RequestHistoryEntry>> EnumerateAsync(RequestHistoryQuery query, CancellationToken token = default)
        {
            query ??= new RequestHistoryQuery();

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
                    cmd.CommandText = "SELECT COUNT(*) FROM request_history" + whereClause + ";";
                    foreach (SqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                List<RequestHistoryEntry> results = new List<RequestHistoryEntry>();
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    int pageSize = query.PageSize <= 0 ? 25 : query.PageSize;
                    cmd.CommandText = "SELECT * FROM request_history" + whereClause
                        + " ORDER BY created_utc DESC OFFSET @offset ROWS FETCH NEXT @page_size ROWS ONLY;";
                    foreach (SqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    cmd.Parameters.AddWithValue("@offset", query.Offset);
                    cmd.Parameters.AddWithValue("@page_size", pageSize);
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(EntryFromReader(reader));
                    }
                }

                return EnumerationResult<RequestHistoryEntry>.Create(
                    new EnumerationQuery { PageNumber = query.PageNumber, PageSize = query.PageSize <= 0 ? 25 : query.PageSize },
                    results,
                    totalCount);
            }
        }

        /// <inheritdoc />
        public async Task<List<RequestHistoryEntry>> EnumerateForSummaryAsync(RequestHistoryQuery query, CancellationToken token = default)
        {
            query ??= new RequestHistoryQuery();

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<SqlParameter> parameters = new List<SqlParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty;

                List<RequestHistoryEntry> results = new List<RequestHistoryEntry>();
                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM request_history" + whereClause + " ORDER BY created_utc DESC;";
                    foreach (SqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(EntryFromReader(reader));
                    }
                }

                return results;
            }
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, RequestHistoryQuery? query = null, CancellationToken token = default)
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
                    cmd.CommandText = "DELETE FROM request_history WHERE " + string.Join(" AND ", conditions) + ";";
                    foreach (SqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<int> DeleteByFilterAsync(RequestHistoryQuery query, CancellationToken token = default)
        {
            query ??= new RequestHistoryQuery();

            using (SqlConnection conn = new SqlConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<SqlParameter> parameters = new List<SqlParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty;

                using (SqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM request_history" + whereClause + ";";
                    foreach (SqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        private static void BindEntry(SqlCommand cmd, RequestHistoryEntry entry)
        {
            cmd.Parameters.AddWithValue("@id", entry.Id);
            cmd.Parameters.AddWithValue("@tenant_id", (object?)entry.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_id", (object?)entry.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@credential_id", (object?)entry.CredentialId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@principal_display", (object?)entry.PrincipalDisplay ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@auth_method", (object?)entry.AuthMethod ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@method", entry.Method);
            cmd.Parameters.AddWithValue("@route", entry.Route);
            cmd.Parameters.AddWithValue("@route_template", (object?)entry.RouteTemplate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@query_string", (object?)entry.QueryString ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@status_code", entry.StatusCode);
            cmd.Parameters.AddWithValue("@duration_ms", entry.DurationMs);
            cmd.Parameters.AddWithValue("@request_size_bytes", entry.RequestSizeBytes);
            cmd.Parameters.AddWithValue("@response_size_bytes", entry.ResponseSizeBytes);
            cmd.Parameters.AddWithValue("@request_content_type", (object?)entry.RequestContentType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@response_content_type", (object?)entry.ResponseContentType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@is_success", entry.IsSuccess);
            cmd.Parameters.AddWithValue("@client_ip", (object?)entry.ClientIp ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@correlation_id", (object?)entry.CorrelationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@created_utc", SqlServerDatabaseDriver.ToIso8601(entry.CreatedUtc));
        }

        private static void BindDetail(SqlCommand cmd, RequestHistoryDetail detail)
        {
            cmd.Parameters.AddWithValue("@request_history_id", detail.RequestHistoryId);
            cmd.Parameters.AddWithValue("@path_params_json", (object?)detail.PathParamsJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@query_params_json", (object?)detail.QueryParamsJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@request_headers_json", (object?)detail.RequestHeadersJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@response_headers_json", (object?)detail.ResponseHeadersJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@request_body_text", (object?)detail.RequestBodyText ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@response_body_text", (object?)detail.ResponseBodyText ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@request_body_truncated", detail.RequestBodyTruncated);
            cmd.Parameters.AddWithValue("@response_body_truncated", detail.ResponseBodyTruncated);
        }

        private static RequestHistoryEntry EntryFromReader(SqlDataReader reader)
        {
            return new RequestHistoryEntry
            {
                Id = reader["id"].ToString()!,
                TenantId = SqlServerDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = SqlServerDatabaseDriver.NullableString(reader["user_id"]),
                CredentialId = SqlServerDatabaseDriver.NullableString(reader["credential_id"]),
                PrincipalDisplay = SqlServerDatabaseDriver.NullableString(reader["principal_display"]),
                AuthMethod = SqlServerDatabaseDriver.NullableString(reader["auth_method"]),
                Method = reader["method"].ToString()!,
                Route = reader["route"].ToString()!,
                RouteTemplate = SqlServerDatabaseDriver.NullableString(reader["route_template"]),
                QueryString = SqlServerDatabaseDriver.NullableString(reader["query_string"]),
                StatusCode = Convert.ToInt32(reader["status_code"]),
                DurationMs = Convert.ToDouble(reader["duration_ms"]),
                RequestSizeBytes = Convert.ToInt64(reader["request_size_bytes"]),
                ResponseSizeBytes = Convert.ToInt64(reader["response_size_bytes"]),
                RequestContentType = SqlServerDatabaseDriver.NullableString(reader["request_content_type"]),
                ResponseContentType = SqlServerDatabaseDriver.NullableString(reader["response_content_type"]),
                IsSuccess = Convert.ToBoolean(reader["is_success"]),
                ClientIp = SqlServerDatabaseDriver.NullableString(reader["client_ip"]),
                CorrelationId = SqlServerDatabaseDriver.NullableString(reader["correlation_id"]),
                CreatedUtc = SqlServerDatabaseDriver.FromIso8601(reader["created_utc"].ToString()!)
            };
        }

        private static RequestHistoryDetail DetailFromReader(SqlDataReader reader)
        {
            return new RequestHistoryDetail
            {
                RequestHistoryId = reader["request_history_id"].ToString()!,
                PathParamsJson = SqlServerDatabaseDriver.NullableString(reader["path_params_json"]),
                QueryParamsJson = SqlServerDatabaseDriver.NullableString(reader["query_params_json"]),
                RequestHeadersJson = SqlServerDatabaseDriver.NullableString(reader["request_headers_json"]),
                ResponseHeadersJson = SqlServerDatabaseDriver.NullableString(reader["response_headers_json"]),
                RequestBodyText = SqlServerDatabaseDriver.NullableString(reader["request_body_text"]),
                ResponseBodyText = SqlServerDatabaseDriver.NullableString(reader["response_body_text"]),
                RequestBodyTruncated = Convert.ToBoolean(reader["request_body_truncated"]),
                ResponseBodyTruncated = Convert.ToBoolean(reader["response_body_truncated"])
            };
        }

        private static SqlParameter CloneParameter(SqlParameter parameter)
        {
            return new SqlParameter(parameter.ParameterName, parameter.Value);
        }

        private static void ApplyQueryFilters(RequestHistoryQuery? query, List<string> conditions, List<SqlParameter> parameters)
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
            if (!string.IsNullOrWhiteSpace(query.CredentialId))
            {
                conditions.Add("credential_id = @credential_id");
                parameters.Add(new SqlParameter("@credential_id", query.CredentialId));
            }
            if (!string.IsNullOrWhiteSpace(query.Principal))
            {
                conditions.Add("principal_display LIKE @principal");
                parameters.Add(new SqlParameter("@principal", "%" + query.Principal + "%"));
            }
            if (!string.IsNullOrWhiteSpace(query.Method))
            {
                conditions.Add("UPPER(method) = @method");
                parameters.Add(new SqlParameter("@method", query.Method.ToUpperInvariant()));
            }
            if (query.ExcludeReadMethods)
                conditions.Add("UPPER(method) NOT IN ('GET', 'HEAD', 'OPTIONS')");
            if (!string.IsNullOrWhiteSpace(query.Route))
            {
                conditions.Add("route LIKE @route");
                parameters.Add(new SqlParameter("@route", "%" + query.Route + "%"));
            }
            if (query.StatusCode.HasValue)
            {
                conditions.Add("status_code = @status_code");
                parameters.Add(new SqlParameter("@status_code", query.StatusCode.Value));
            }
            if (query.IsSuccess.HasValue)
            {
                conditions.Add("is_success = @is_success");
                parameters.Add(new SqlParameter("@is_success", query.IsSuccess.Value));
            }
            if (query.FromUtc.HasValue)
            {
                conditions.Add("created_utc >= @from_utc");
                parameters.Add(new SqlParameter("@from_utc", SqlServerDatabaseDriver.ToIso8601(query.FromUtc.Value)));
            }
            if (query.ToUtc.HasValue)
            {
                conditions.Add("created_utc <= @to_utc");
                parameters.Add(new SqlParameter("@to_utc", SqlServerDatabaseDriver.ToIso8601(query.ToUtc.Value)));
            }
        }
    }
}
