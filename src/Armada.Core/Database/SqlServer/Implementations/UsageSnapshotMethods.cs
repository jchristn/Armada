namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Database.SqlServer.Queries;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services;
    using SyslogLogging;

    /// <summary>
    /// SQL Server implementation of usage snapshot persistence: snapshots, their captain member links and the
    /// per-account latest state. A snapshot and its links are written in one transaction; latest state is
    /// keyed by tenant, host, runtime and account key and upserted so a failed capture can carry the error
    /// without erasing the last good summary.
    /// </summary>
    public class UsageSnapshotMethods : IUsageSnapshotMethods
    {
        #region Private-Members

        private readonly string _ConnectionString;
        private readonly SemaphoreSlim? _WriteLock;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQL Server database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public UsageSnapshotMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _ConnectionString = driver.ConnectionString;
            _WriteLock = null;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<UsageSnapshot> CreateAsync(UsageSnapshot snapshot, List<string>? captainIds, CancellationToken token = default)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (String.IsNullOrEmpty(snapshot.TenantId)) throw new ArgumentNullException(nameof(snapshot), "Snapshot tenant id is required.");
            if (String.IsNullOrEmpty(snapshot.HostId)) throw new ArgumentNullException(nameof(snapshot), "Snapshot host id is required.");

            List<string> members = (captainIds ?? new List<string>())
                .Where(id => !String.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.SnapshotInsert, cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@id", snapshot.Id);
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", snapshot.TenantId);
                    SqlServerCommandHelper.Add(cmd, "@captain_id", String.IsNullOrEmpty(snapshot.CaptainId) ? null : snapshot.CaptainId);
                    SqlServerCommandHelper.Add(cmd, "@harbor_id", String.IsNullOrEmpty(snapshot.HarborId) ? null : snapshot.HarborId);
                    SqlServerCommandHelper.Add(cmd, "@host_id", snapshot.HostId);
                    SqlServerCommandHelper.Add(cmd, "@runtime", snapshot.Runtime.ToString());
                    SqlServerCommandHelper.Add(cmd, "@provider", String.IsNullOrEmpty(snapshot.Provider) ? null : snapshot.Provider);
                    SqlServerCommandHelper.Add(cmd, "@account_key", String.IsNullOrEmpty(snapshot.AccountKey) ? null : snapshot.AccountKey);
                    SqlServerCommandHelper.AddDate(cmd, "@captured_utc", snapshot.CapturedUtc);
                    SqlServerCommandHelper.AddDate(cmd, "@received_utc", snapshot.ReceivedUtc);
                    SqlServerCommandHelper.Add(cmd, "@status", snapshot.Status.ToString());
                    SqlServerCommandHelper.Add(cmd, "@source", snapshot.Source.ToString());
                    SqlServerCommandHelper.Add(cmd, "@windows_json", snapshot.WindowsJson);
                    SqlServerCommandHelper.Add(cmd, "@raw_excerpt", snapshot.RawExcerpt);
                    SqlServerCommandHelper.Add(cmd, "@error_code", String.IsNullOrEmpty(snapshot.ErrorCode) ? null : snapshot.ErrorCode);
                }, token).ConfigureAwait(false);

                foreach (string captainId in members)
                {
                    string linkId = Constants.IdGenerator.GenerateKSortable(Constants.UsageSnapshotCaptainIdPrefix, 24);
                    await SqlServerCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.SnapshotCaptainsInsert, cmd =>
                    {
                        SqlServerCommandHelper.Add(cmd, "@id", linkId);
                        SqlServerCommandHelper.Add(cmd, "@tenant_id", snapshot.TenantId);
                        SqlServerCommandHelper.Add(cmd, "@snapshot_id", snapshot.Id);
                        SqlServerCommandHelper.Add(cmd, "@captain_id", captainId);
                        SqlServerCommandHelper.AddDate(cmd, "@captured_utc", snapshot.CapturedUtc);
                    }, token).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);

            return snapshot;
        }

        /// <inheritdoc />
        public async Task<UsageSnapshot?> ReadAsync(string id, string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));

            List<UsageSnapshot> rows = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                UsageSnapshotQueries.SnapshotSelectById,
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@id", id);
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<UsageSnapshot>> EnumerateAsync(UsageSnapshotQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            int pageSize = query.PageSize < 1 ? 1 : (query.PageSize > 1000 ? 1000 : query.PageSize);
            int pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            int offset = (pageNumber - 1) * pageSize;

            List<string> conditions = new List<string>();
            Action<SqlCommand> bind = cmd => { };

            if (query.TenantId != null)
            {
                conditions.Add("tenant_id = @tenant_id");
                bind += cmd => SqlServerCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
            }
            if (query.CaptainId != null)
            {
                conditions.Add("(captain_id = @captain_id OR EXISTS (SELECT 1 FROM usage_snapshot_captains l WHERE l.snapshot_id = usage_snapshots.id AND l.captain_id = @captain_id))");
                bind += cmd => SqlServerCommandHelper.Add(cmd, "@captain_id", query.CaptainId);
            }
            if (query.HarborId != null)
            {
                conditions.Add("harbor_id = @harbor_id");
                bind += cmd => SqlServerCommandHelper.Add(cmd, "@harbor_id", query.HarborId);
            }
            if (query.Runtime.HasValue)
            {
                conditions.Add("runtime = @runtime");
                bind += cmd => SqlServerCommandHelper.Add(cmd, "@runtime", query.Runtime.Value.ToString());
            }
            if (query.Provider != null)
            {
                conditions.Add("provider = @provider");
                bind += cmd => SqlServerCommandHelper.Add(cmd, "@provider", query.Provider);
            }
            if (query.AccountKey != null)
            {
                conditions.Add("account_key = @account_key");
                bind += cmd => SqlServerCommandHelper.Add(cmd, "@account_key", query.AccountKey);
            }
            if (query.Status.HasValue)
            {
                conditions.Add("status = @status");
                bind += cmd => SqlServerCommandHelper.Add(cmd, "@status", query.Status.Value.ToString());
            }
            if (query.FromUtc.HasValue)
            {
                conditions.Add("captured_utc >= @from_utc");
                bind += cmd => SqlServerCommandHelper.AddDate(cmd, "@from_utc", query.FromUtc.Value);
            }
            if (query.ToUtc.HasValue)
            {
                conditions.Add("captured_utc < @to_utc");
                bind += cmd => SqlServerCommandHelper.AddDate(cmd, "@to_utc", query.ToUtc.Value);
            }

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqlServerCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM usage_snapshots" + where + ";", bind, token).ConfigureAwait(false);
                List<UsageSnapshot> rows = await SqlServerCommandHelper.QueryAsync(conn,
                    "SELECT * FROM usage_snapshots" + where + " ORDER BY captured_utc DESC, id DESC" + SqlServerCommandHelper.Page(offset, pageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);

                EnumerationResult<UsageSnapshot> result = new EnumerationResult<UsageSnapshot>();
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalRecords = total;
                result.TotalPages = (int)Math.Ceiling((double)total / pageSize);
                result.Objects = rows;
                return result;
            }
        }

        /// <inheritdoc />
        public async Task<List<string>> ReadCaptainsAsync(string snapshotId, string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(snapshotId)) throw new ArgumentNullException(nameof(snapshotId));
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));

            List<string> rows = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                UsageSnapshotQueries.SnapshotCaptainsSelect,
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@snapshot_id", snapshotId);
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, r => r["captain_id"].ToString()!, token).ConfigureAwait(false);
            return rows;
        }

        /// <inheritdoc />
        public async Task<int> DeleteOlderThanAsync(DateTime capturedBeforeUtc, CancellationToken token = default)
        {
            int deleted = 0;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                List<UsageSnapshot> expired = await SqlServerCommandHelper.QueryAsync(conn, tx,
                    "SELECT TOP (500) * FROM usage_snapshots WITH (UPDLOCK, HOLDLOCK) WHERE captured_utc < @captured_before ORDER BY captured_utc, id;",
                    cmd => SqlServerCommandHelper.AddDate(cmd, "@captured_before", capturedBeforeUtc), FromReader, token).ConfigureAwait(false);
                foreach (IGrouping<string, UsageSnapshot> group in expired.Where(row => !String.IsNullOrEmpty(row.AccountKey))
                    .GroupBy(UsageRollupBuilder.GroupKey))
                {
                    UsageSnapshot first = group.First();
                    List<UsageHourlyRollup> existing = await SqlServerCommandHelper.QueryAsync(conn, tx,
                        "SELECT * FROM usage_hourly_rollups WHERE tenant_id = @tenant_id AND host_id = @host_id AND runtime = @runtime AND provider = @provider AND account_key = @account_key AND hour_utc = @hour_utc;",
                        cmd =>
                        {
                            SqlServerCommandHelper.Add(cmd, "@tenant_id", first.TenantId);
                            SqlServerCommandHelper.Add(cmd, "@host_id", first.HostId);
                            SqlServerCommandHelper.Add(cmd, "@runtime", first.Runtime.ToString());
                            SqlServerCommandHelper.Add(cmd, "@provider", first.Provider ?? String.Empty);
                            SqlServerCommandHelper.Add(cmd, "@account_key", first.AccountKey);
                            SqlServerCommandHelper.AddDate(cmd, "@hour_utc", UsageRollupBuilder.Hour(first.CapturedUtc));
                        }, UsageHourlyRollupMethods.FromReader, token).ConfigureAwait(false);
                    foreach (UsageHourlyRollup rollup in UsageRollupBuilder.Merge(group.ToList(), existing))
                    {
                        await SqlServerCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.RollupUpsert,
                            cmd => UsageHourlyRollupMethods.BindRollup(cmd, rollup), token).ConfigureAwait(false);
                    }
                }

                if (expired.Count == 0) return;
                string ids = String.Join(",", expired.Select((row, index) => "@snapshot_" + index));
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM usage_snapshot_captains WHERE snapshot_id IN (" + ids + ");", cmd =>
                {
                    for (int index = 0; index < expired.Count; index++) SqlServerCommandHelper.Add(cmd, "@snapshot_" + index, expired[index].Id);
                }, token).ConfigureAwait(false);
                deleted = await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM usage_snapshots WHERE id IN (" + ids + ");", cmd =>
                {
                    for (int index = 0; index < expired.Count; index++) SqlServerCommandHelper.Add(cmd, "@snapshot_" + index, expired[index].Id);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        /// <inheritdoc />
        public async Task<UsageLatestState> UpsertLatestStateAsync(UsageLatestState state, CancellationToken token = default)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (String.IsNullOrEmpty(state.TenantId)) throw new ArgumentNullException(nameof(state), "Latest state tenant id is required.");
            if (String.IsNullOrEmpty(state.HostId)) throw new ArgumentNullException(nameof(state), "Latest state host id is required.");
            if (String.IsNullOrEmpty(state.AccountKey)) throw new ArgumentNullException(nameof(state), "Latest state account key is required.");
            state.LastUpdateUtc = DateTime.UtcNow;

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.LatestStateUpsert, cmd => BindLatestState(cmd, state), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return state;
        }

        /// <inheritdoc />
        public async Task<UsageLatestState?> ReadLatestStateAsync(string tenantId, string hostId, AgentRuntimeEnum runtime, string accountKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(hostId)) throw new ArgumentNullException(nameof(hostId));
            if (String.IsNullOrEmpty(accountKey)) throw new ArgumentNullException(nameof(accountKey));

            List<UsageLatestState> rows = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                UsageSnapshotQueries.LatestStateSelectByKey,
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@host_id", hostId);
                    SqlServerCommandHelper.Add(cmd, "@runtime", runtime.ToString());
                    SqlServerCommandHelper.Add(cmd, "@account_key", accountKey);
                }, LatestStateFromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<UsageLatestState>> EnumerateLatestStatesAsync(UsageLatestStateQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            int pageSize = query.PageSize < 1 ? 1 : (query.PageSize > 1000 ? 1000 : query.PageSize);
            int pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            int offset = (pageNumber - 1) * pageSize;

            List<string> conditions = new List<string>();
            Action<SqlCommand> bind = cmd => { };

            if (query.TenantId != null)
            {
                conditions.Add("tenant_id = @tenant_id");
                bind += cmd => SqlServerCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
            }
            if (query.HostId != null)
            {
                conditions.Add("host_id = @host_id");
                bind += cmd => SqlServerCommandHelper.Add(cmd, "@host_id", query.HostId);
            }
            if (query.Runtime.HasValue)
            {
                conditions.Add("runtime = @runtime");
                bind += cmd => SqlServerCommandHelper.Add(cmd, "@runtime", query.Runtime.Value.ToString());
            }
            if (query.Provider != null)
            {
                conditions.Add("provider = @provider");
                bind += cmd => SqlServerCommandHelper.Add(cmd, "@provider", query.Provider);
            }
            if (query.AccountKey != null)
            {
                conditions.Add("account_key = @account_key");
                bind += cmd => SqlServerCommandHelper.Add(cmd, "@account_key", query.AccountKey);
            }
            if (!query.IncludeExpired)
            {
                conditions.Add("expired_utc IS NULL");
            }

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqlServerCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM usage_latest_state" + where + ";", bind, token).ConfigureAwait(false);
                List<UsageLatestState> rows = await SqlServerCommandHelper.QueryAsync(conn,
                    "SELECT * FROM usage_latest_state" + where + " ORDER BY account_key ASC, id ASC" + SqlServerCommandHelper.Page(offset, pageSize) + ";",
                    bind, LatestStateFromReader, token).ConfigureAwait(false);

                EnumerationResult<UsageLatestState> result = new EnumerationResult<UsageLatestState>();
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalRecords = total;
                result.TotalPages = (int)Math.Ceiling((double)total / pageSize);
                result.Objects = rows;
                return result;
            }
        }

        /// <inheritdoc />
        public async Task<int> PruneInactiveStateAsync(DateTime closedBeforeUtc, CancellationToken token = default)
        {
            int deleted = 0;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                deleted += await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM usage_latest_state WHERE expired_utc < @cutoff;",
                    cmd => SqlServerCommandHelper.AddDate(cmd, "@cutoff", closedBeforeUtc), token).ConfigureAwait(false);
                deleted += await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM usage_account_memberships WHERE effective_to_utc < @cutoff;",
                    cmd => SqlServerCommandHelper.AddDate(cmd, "@cutoff", closedBeforeUtc), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        /// <inheritdoc />
        public async Task<int> DeleteByTenantAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));

            int deleted = 0;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.SnapshotCaptainsDeleteByTenant, cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, token).ConfigureAwait(false);

                await SqlServerCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.LatestStateDeleteByTenant, cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, token).ConfigureAwait(false);

                deleted = await SqlServerCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.SnapshotDeleteByTenant, cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void BindLatestState(SqlCommand cmd, UsageLatestState state)
        {
            SqlServerCommandHelper.Add(cmd, "@id", state.Id);
            SqlServerCommandHelper.Add(cmd, "@tenant_id", state.TenantId);
            SqlServerCommandHelper.Add(cmd, "@host_id", state.HostId);
            SqlServerCommandHelper.Add(cmd, "@runtime", state.Runtime.ToString());
            SqlServerCommandHelper.Add(cmd, "@provider", String.IsNullOrEmpty(state.Provider) ? null : state.Provider);
            SqlServerCommandHelper.Add(cmd, "@account_key", state.AccountKey);
            SqlServerCommandHelper.Add(cmd, "@last_good_snapshot_id", String.IsNullOrEmpty(state.LastGoodSnapshotId) ? null : state.LastGoodSnapshotId);
            SqlServerCommandHelper.AddDate(cmd, "@last_good_captured_utc", state.LastGoodCapturedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_good_received_utc", state.LastGoodReceivedUtc);
            SqlServerCommandHelper.Add(cmd, "@last_good_status", state.LastGoodStatus?.ToString());
            SqlServerCommandHelper.Add(cmd, "@last_good_windows_json", state.LastGoodWindowsJson);
            SqlServerCommandHelper.Add(cmd, "@last_error_snapshot_id", String.IsNullOrEmpty(state.LastErrorSnapshotId) ? null : state.LastErrorSnapshotId);
            SqlServerCommandHelper.AddDate(cmd, "@last_error_captured_utc", state.LastErrorCapturedUtc);
            SqlServerCommandHelper.Add(cmd, "@last_error_status", state.LastErrorStatus?.ToString());
            SqlServerCommandHelper.Add(cmd, "@last_error_code", state.LastErrorCode);
            SqlServerCommandHelper.AddDate(cmd, "@expired_utc", state.ExpiredUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", state.LastUpdateUtc);
        }

        private static UsageSnapshot FromReader(SqlDataReader reader)
        {
            UsageSnapshot snapshot = new UsageSnapshot();
            snapshot.Id = reader["id"].ToString()!;
            snapshot.TenantId = reader["tenant_id"].ToString()!;
            snapshot.CaptainId = SqlServerCommandHelper.ReadString(reader["captain_id"]);
            snapshot.HarborId = SqlServerCommandHelper.ReadString(reader["harbor_id"]);
            snapshot.HostId = reader["host_id"].ToString()!;
            snapshot.Runtime = SqlServerCommandHelper.ReadEnum(reader["runtime"], AgentRuntimeEnum.ClaudeCode);
            snapshot.Provider = SqlServerCommandHelper.ReadString(reader["provider"]);
            snapshot.AccountKey = SqlServerCommandHelper.ReadString(reader["account_key"]);
            snapshot.CapturedUtc = SqlServerCommandHelper.ReadDate(reader["captured_utc"]);
            snapshot.ReceivedUtc = SqlServerCommandHelper.ReadDate(reader["received_utc"]);
            snapshot.Status = SqlServerCommandHelper.ReadEnum(reader["status"], UsageSnapshotStatusEnum.Failed);
            snapshot.Source = SqlServerCommandHelper.ReadEnum(reader["source"], UsageCaptureSourceEnum.OAuthEndpoint);
            snapshot.WindowsJson = SqlServerCommandHelper.ReadString(reader["windows_json"]);
            snapshot.RawExcerpt = SqlServerCommandHelper.ReadString(reader["raw_excerpt"]);
            snapshot.ErrorCode = SqlServerCommandHelper.ReadString(reader["error_code"]);
            return snapshot;
        }

        private static UsageLatestState LatestStateFromReader(SqlDataReader reader)
        {
            UsageLatestState state = new UsageLatestState();
            state.Id = reader["id"].ToString()!;
            state.TenantId = reader["tenant_id"].ToString()!;
            state.HostId = reader["host_id"].ToString()!;
            state.Runtime = SqlServerCommandHelper.ReadEnum(reader["runtime"], AgentRuntimeEnum.ClaudeCode);
            state.Provider = SqlServerCommandHelper.ReadString(reader["provider"]);
            state.AccountKey = reader["account_key"].ToString()!;
            state.LastGoodSnapshotId = SqlServerCommandHelper.ReadString(reader["last_good_snapshot_id"]);
            state.LastGoodCapturedUtc = SqlServerCommandHelper.ReadNullableDate(reader["last_good_captured_utc"]);
            state.LastGoodReceivedUtc = SqlServerCommandHelper.ReadNullableDate(reader["last_good_received_utc"]);
            state.LastGoodStatus = ReadNullableStatus(reader["last_good_status"]);
            state.LastGoodWindowsJson = SqlServerCommandHelper.ReadString(reader["last_good_windows_json"]);
            state.LastErrorSnapshotId = SqlServerCommandHelper.ReadString(reader["last_error_snapshot_id"]);
            state.LastErrorCapturedUtc = SqlServerCommandHelper.ReadNullableDate(reader["last_error_captured_utc"]);
            state.LastErrorStatus = ReadNullableStatus(reader["last_error_status"]);
            state.LastErrorCode = SqlServerCommandHelper.ReadString(reader["last_error_code"]);
            state.ExpiredUtc = SqlServerCommandHelper.ReadNullableDate(reader["expired_utc"]);
            state.LastUpdateUtc = SqlServerCommandHelper.ReadDate(reader["last_update_utc"]);
            return state;
        }

        private static UsageSnapshotStatusEnum? ReadNullableStatus(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string? text = SqlServerCommandHelper.ReadString(value);
            if (String.IsNullOrEmpty(text)) return null;
            if (Enum.TryParse<UsageSnapshotStatusEnum>(text, true, out UsageSnapshotStatusEnum parsed)) return parsed;
            return null;
        }

        #endregion
    }
}