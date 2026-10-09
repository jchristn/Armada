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
    /// SQLite implementation of request-history database operations.
    /// </summary>
    public class RequestHistoryMethods : IRequestHistoryMethods
    {
        #region Private-Members

#pragma warning disable CS0414
        private readonly string _Header = "[RequestHistoryMethods] ";
#pragma warning restore CS0414
        private readonly SqliteDatabaseDriver _Driver;
        private readonly DatabaseSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly object _PendingLock = new object();
        private List<RequestHistoryPendingInsert> _Pending = new List<RequestHistoryPendingInsert>();
        private bool _Flushing = false;

        private const int _MaxBatchSize = 256;
        private const int _DeleteChunkSize = 1000;

        private const string _InsertEntrySql = @"INSERT INTO request_history (
                                id, tenant_id, user_id, credential_id, principal_display, auth_method, method, route, route_template,
                                query_string, status_code, duration_ms, request_size_bytes, response_size_bytes, request_content_type,
                                response_content_type, is_success, client_ip, correlation_id, created_utc
                            ) VALUES (
                                @id, @tenant_id, @user_id, @credential_id, @principal_display, @auth_method, @method, @route, @route_template,
                                @query_string, @status_code, @duration_ms, @request_size_bytes, @response_size_bytes, @request_content_type,
                                @response_content_type, @is_success, @client_ip, @correlation_id, @created_utc
                            );";

        private const string _InsertDetailSql = @"INSERT INTO request_history_detail (
                                    request_history_id, path_params_json, query_params_json, request_headers_json, response_headers_json,
                                    request_body_text, response_body_text, request_body_truncated, response_body_truncated
                                ) VALUES (
                                    @request_history_id, @path_params_json, @query_params_json, @request_headers_json, @response_headers_json,
                                    @request_body_text, @response_body_text, @request_body_truncated, @response_body_truncated
                                );";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RequestHistoryMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        /// <remarks>
        /// Inserts are group-committed: concurrent calls (one per captured REST request) are queued and written by a
        /// single flusher in one transaction per batch under the database's write gate, so a burst of requests costs
        /// one turn at the gate instead of one each. The call still completes only once its row is committed.
        /// Cancelling the token stops the wait, not the insert.
        /// </remarks>
        public async Task<RequestHistoryRecord> CreateAsync(RequestHistoryEntry entry, RequestHistoryDetail? detail, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            token.ThrowIfCancellationRequested();

            RequestHistoryPendingInsert pending = new RequestHistoryPendingInsert(entry, detail);
            bool startFlusher = false;
            lock (_PendingLock)
            {
                _Pending.Add(pending);
                if (!_Flushing)
                {
                    _Flushing = true;
                    startFlusher = true;
                }
            }

            if (startFlusher)
            {
                // The flusher runs on a clean execution context: it must not inherit the caller's write-gate marker.
                using (ExecutionContext.SuppressFlow())
                {
                    _ = Task.Run(FlushPendingAsync);
                }
            }

            await pending.Completion.Task.WaitAsync(token).ConfigureAwait(false);
            return new RequestHistoryRecord { Entry = entry, Detail = detail };
        }

        /// <inheritdoc />
        public async Task<RequestHistoryRecord?> ReadAsync(string id, RequestHistoryQuery? query = null, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string> { "id = @id" };
                List<SqliteParameter> parameters = new List<SqliteParameter> { new SqliteParameter("@id", id) };
                ApplyQueryFilters(query, conditions, parameters);

                RequestHistoryEntry? entry = null;
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM request_history WHERE " + string.Join(" AND ", conditions) + " LIMIT 1;";
                    foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            entry = EntryFromReader(reader);
                    }
                }

                if (entry == null) return null;

                RequestHistoryDetail? detail = null;
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM request_history_detail WHERE request_history_id = @id LIMIT 1;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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
                    cmd.CommandText = "SELECT COUNT(*) FROM request_history" + whereClause + ";";
                    foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                List<RequestHistoryEntry> results = new List<RequestHistoryEntry>();
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    int pageSize = query.PageSize <= 0 ? 25 : query.PageSize;
                    int offset = query.PageNumber <= 1 ? 0 : (query.PageNumber - 1) * pageSize;
                    cmd.CommandText = "SELECT * FROM request_history" + whereClause
                        + " ORDER BY created_utc DESC LIMIT " + pageSize + " OFFSET " + offset + ";";
                    foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<SqliteParameter> parameters = new List<SqliteParameter>();
                ApplyQueryFilters(query, conditions, parameters);

                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty;
                List<RequestHistoryEntry> results = new List<RequestHistoryEntry>();
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM request_history" + whereClause + " ORDER BY created_utc DESC;";
                    foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                List<string> conditions = new List<string> { "id = @id" };
                List<SqliteParameter> parameters = new List<SqliteParameter> { new SqliteParameter("@id", id) };
                ApplyQueryFilters(query, conditions, parameters);

                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM request_history WHERE " + string.Join(" AND ", conditions) + ";";
                    foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// Deletes in chunks, each its own transaction and its own turn at the write gate, so a large retention sweep
        /// never holds the gate for long: writers queued behind it wait for one chunk, not the whole sweep.
        /// </remarks>
        public async Task<int> DeleteByFilterAsync(RequestHistoryQuery query, CancellationToken token = default)
        {
            query ??= new RequestHistoryQuery();

            List<string> conditions = new List<string>();
            List<SqliteParameter> parameters = new List<SqliteParameter>();
            ApplyQueryFilters(query, conditions, parameters);
            string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty;
            string sql = "DELETE FROM request_history WHERE rowid IN (SELECT rowid FROM request_history" + whereClause
                + " LIMIT " + _DeleteChunkSize + ");";

            int total = 0;
            while (true)
            {
                int deleted;
                using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
                using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
                {
                    await conn.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = sql;
                        foreach (SqliteParameter parameter in parameters)
                            cmd.Parameters.Add(new SqliteParameter(parameter.ParameterName, parameter.Value));
                        deleted = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }

                total += deleted;
                if (deleted < _DeleteChunkSize) return total;
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Write queued inserts until the queue is empty. Each pass takes the write gate, then takes everything queued
        /// so far (up to the batch size) and commits it in one transaction. If the batch fails, its rows are retried one
        /// at a time so only the failing row reports the error.
        /// </summary>
        private async Task FlushPendingAsync()
        {
            while (true)
            {
                using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync().ConfigureAwait(false))
                {
                    List<RequestHistoryPendingInsert> batch;
                    lock (_PendingLock)
                    {
                        if (_Pending.Count == 0)
                        {
                            _Flushing = false;
                            return;
                        }

                        if (_Pending.Count <= _MaxBatchSize)
                        {
                            batch = _Pending;
                            _Pending = new List<RequestHistoryPendingInsert>();
                        }
                        else
                        {
                            batch = _Pending.GetRange(0, _MaxBatchSize);
                            _Pending.RemoveRange(0, _MaxBatchSize);
                        }
                    }

                    try
                    {
                        await InsertBatchAsync(batch).ConfigureAwait(false);
                        foreach (RequestHistoryPendingInsert pending in batch) pending.Completion.TrySetResult(true);
                    }
                    catch (Exception batchEx)
                    {
                        if (batch.Count == 1)
                        {
                            batch[0].Completion.TrySetException(batchEx);
                            continue;
                        }

                        foreach (RequestHistoryPendingInsert pending in batch)
                        {
                            try
                            {
                                await InsertBatchAsync(new List<RequestHistoryPendingInsert> { pending }).ConfigureAwait(false);
                                pending.Completion.TrySetResult(true);
                            }
                            catch (Exception ex)
                            {
                                pending.Completion.TrySetException(ex);
                            }
                        }
                    }
                }
            }
        }

        private async Task InsertBatchAsync(List<RequestHistoryPendingInsert> batch)
        {
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (SqliteTransaction tx = conn.BeginTransaction())
                {
                    foreach (RequestHistoryPendingInsert pending in batch)
                    {
                        using (SqliteCommand cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = _InsertEntrySql;
                            BindEntry(cmd, pending.Entry);
                            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                        }

                        if (pending.Detail != null)
                        {
                            using (SqliteCommand cmd = conn.CreateCommand())
                            {
                                cmd.Transaction = tx;
                                cmd.CommandText = _InsertDetailSql;
                                BindDetail(cmd, pending.Detail);
                                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                            }
                        }
                    }

                    tx.Commit();
                }
            }
        }

        private static void BindEntry(SqliteCommand cmd, RequestHistoryEntry entry)
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
            cmd.Parameters.AddWithValue("@is_success", entry.IsSuccess ? 1 : 0);
            cmd.Parameters.AddWithValue("@client_ip", (object?)entry.ClientIp ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@correlation_id", (object?)entry.CorrelationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@created_utc", SqliteDatabaseDriver.ToIso8601(entry.CreatedUtc));
        }

        private static void BindDetail(SqliteCommand cmd, RequestHistoryDetail detail)
        {
            cmd.Parameters.AddWithValue("@request_history_id", detail.RequestHistoryId);
            cmd.Parameters.AddWithValue("@path_params_json", (object?)detail.PathParamsJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@query_params_json", (object?)detail.QueryParamsJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@request_headers_json", (object?)detail.RequestHeadersJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@response_headers_json", (object?)detail.ResponseHeadersJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@request_body_text", (object?)detail.RequestBodyText ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@response_body_text", (object?)detail.ResponseBodyText ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@request_body_truncated", detail.RequestBodyTruncated ? 1 : 0);
            cmd.Parameters.AddWithValue("@response_body_truncated", detail.ResponseBodyTruncated ? 1 : 0);
        }

        private static RequestHistoryEntry EntryFromReader(SqliteDataReader reader)
        {
            return new RequestHistoryEntry
            {
                Id = reader["id"].ToString()!,
                TenantId = NullableString(reader["tenant_id"]),
                UserId = NullableString(reader["user_id"]),
                CredentialId = NullableString(reader["credential_id"]),
                PrincipalDisplay = NullableString(reader["principal_display"]),
                AuthMethod = NullableString(reader["auth_method"]),
                Method = reader["method"].ToString()!,
                Route = reader["route"].ToString()!,
                RouteTemplate = NullableString(reader["route_template"]),
                QueryString = NullableString(reader["query_string"]),
                StatusCode = Convert.ToInt32(reader["status_code"]),
                DurationMs = Convert.ToDouble(reader["duration_ms"]),
                RequestSizeBytes = Convert.ToInt64(reader["request_size_bytes"]),
                ResponseSizeBytes = Convert.ToInt64(reader["response_size_bytes"]),
                RequestContentType = NullableString(reader["request_content_type"]),
                ResponseContentType = NullableString(reader["response_content_type"]),
                IsSuccess = Convert.ToInt32(reader["is_success"]) == 1,
                ClientIp = NullableString(reader["client_ip"]),
                CorrelationId = NullableString(reader["correlation_id"]),
                CreatedUtc = SqliteDatabaseDriver.FromIso8601(reader["created_utc"].ToString()!)
            };
        }

        private static RequestHistoryDetail DetailFromReader(SqliteDataReader reader)
        {
            return new RequestHistoryDetail
            {
                RequestHistoryId = reader["request_history_id"].ToString()!,
                PathParamsJson = NullableString(reader["path_params_json"]),
                QueryParamsJson = NullableString(reader["query_params_json"]),
                RequestHeadersJson = NullableString(reader["request_headers_json"]),
                ResponseHeadersJson = NullableString(reader["response_headers_json"]),
                RequestBodyText = NullableString(reader["request_body_text"]),
                ResponseBodyText = NullableString(reader["response_body_text"]),
                RequestBodyTruncated = Convert.ToInt32(reader["request_body_truncated"]) == 1,
                ResponseBodyTruncated = Convert.ToInt32(reader["response_body_truncated"]) == 1
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

        private static void ApplyQueryFilters(RequestHistoryQuery? query, List<string> conditions, List<SqliteParameter> parameters)
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
            if (!string.IsNullOrWhiteSpace(query.CredentialId))
            {
                conditions.Add("credential_id = @credential_id");
                parameters.Add(new SqliteParameter("@credential_id", query.CredentialId));
            }
            if (!string.IsNullOrWhiteSpace(query.Principal))
            {
                conditions.Add("principal_display LIKE @principal");
                parameters.Add(new SqliteParameter("@principal", "%" + query.Principal + "%"));
            }
            if (!string.IsNullOrWhiteSpace(query.Method))
            {
                conditions.Add("UPPER(method) = @method");
                parameters.Add(new SqliteParameter("@method", query.Method.ToUpperInvariant()));
            }
            if (!string.IsNullOrWhiteSpace(query.Route))
            {
                conditions.Add("route LIKE @route");
                parameters.Add(new SqliteParameter("@route", "%" + query.Route + "%"));
            }
            if (query.StatusCode.HasValue)
            {
                conditions.Add("status_code = @status_code");
                parameters.Add(new SqliteParameter("@status_code", query.StatusCode.Value));
            }
            if (query.IsSuccess.HasValue)
            {
                conditions.Add("is_success = @is_success");
                parameters.Add(new SqliteParameter("@is_success", query.IsSuccess.Value ? 1 : 0));
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
