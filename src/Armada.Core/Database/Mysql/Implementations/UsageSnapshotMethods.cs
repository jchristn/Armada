namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Database.Mysql.Queries;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// MySQL implementation of usage snapshot persistence: snapshots, their captain member links and the
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
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when connectionString is null.</exception>
        public UsageSnapshotMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
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

            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.SnapshotInsert, cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@id", snapshot.Id);
                    MysqlCommandHelper.Add(cmd, "@tenant_id", snapshot.TenantId);
                    MysqlCommandHelper.Add(cmd, "@captain_id", String.IsNullOrEmpty(snapshot.CaptainId) ? null : snapshot.CaptainId);
                    MysqlCommandHelper.Add(cmd, "@harbor_id", String.IsNullOrEmpty(snapshot.HarborId) ? null : snapshot.HarborId);
                    MysqlCommandHelper.Add(cmd, "@host_id", snapshot.HostId);
                    MysqlCommandHelper.Add(cmd, "@runtime", snapshot.Runtime.ToString());
                    MysqlCommandHelper.Add(cmd, "@provider", String.IsNullOrEmpty(snapshot.Provider) ? null : snapshot.Provider);
                    MysqlCommandHelper.Add(cmd, "@account_key", String.IsNullOrEmpty(snapshot.AccountKey) ? null : snapshot.AccountKey);
                    MysqlCommandHelper.AddDate(cmd, "@captured_utc", snapshot.CapturedUtc);
                    MysqlCommandHelper.AddDate(cmd, "@received_utc", snapshot.ReceivedUtc);
                    MysqlCommandHelper.Add(cmd, "@status", snapshot.Status.ToString());
                    MysqlCommandHelper.Add(cmd, "@source", snapshot.Source.ToString());
                    MysqlCommandHelper.Add(cmd, "@windows_json", snapshot.WindowsJson);
                    MysqlCommandHelper.Add(cmd, "@raw_excerpt", snapshot.RawExcerpt);
                    MysqlCommandHelper.Add(cmd, "@error_code", String.IsNullOrEmpty(snapshot.ErrorCode) ? null : snapshot.ErrorCode);
                }, token).ConfigureAwait(false);

                foreach (string captainId in members)
                {
                    string linkId = Constants.IdGenerator.GenerateKSortable(Constants.UsageSnapshotCaptainIdPrefix, 24);
                    await MysqlCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.SnapshotCaptainsInsert, cmd =>
                    {
                        MysqlCommandHelper.Add(cmd, "@id", linkId);
                        MysqlCommandHelper.Add(cmd, "@tenant_id", snapshot.TenantId);
                        MysqlCommandHelper.Add(cmd, "@snapshot_id", snapshot.Id);
                        MysqlCommandHelper.Add(cmd, "@captain_id", captainId);
                        MysqlCommandHelper.AddDate(cmd, "@captured_utc", snapshot.CapturedUtc);
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

            List<UsageSnapshot> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                UsageSnapshotQueries.SnapshotSelectById,
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@id", id);
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
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
            Action<MySqlCommand> bind = cmd => { };

            if (query.TenantId != null)
            {
                conditions.Add("tenant_id = @tenant_id");
                bind += cmd => MysqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
            }
            if (query.CaptainId != null)
            {
                conditions.Add("(captain_id = @captain_id OR EXISTS (SELECT 1 FROM usage_snapshot_captains l WHERE l.snapshot_id = usage_snapshots.id AND l.captain_id = @captain_id))");
                bind += cmd => MysqlCommandHelper.Add(cmd, "@captain_id", query.CaptainId);
            }
            if (query.HarborId != null)
            {
                conditions.Add("harbor_id = @harbor_id");
                bind += cmd => MysqlCommandHelper.Add(cmd, "@harbor_id", query.HarborId);
            }
            if (query.Runtime.HasValue)
            {
                conditions.Add("runtime = @runtime");
                bind += cmd => MysqlCommandHelper.Add(cmd, "@runtime", query.Runtime.Value.ToString());
            }
            if (query.Provider != null)
            {
                conditions.Add("provider = @provider");
                bind += cmd => MysqlCommandHelper.Add(cmd, "@provider", query.Provider);
            }
            if (query.AccountKey != null)
            {
                conditions.Add("account_key = @account_key");
                bind += cmd => MysqlCommandHelper.Add(cmd, "@account_key", query.AccountKey);
            }
            if (query.Status.HasValue)
            {
                conditions.Add("status = @status");
                bind += cmd => MysqlCommandHelper.Add(cmd, "@status", query.Status.Value.ToString());
            }
            if (query.FromUtc.HasValue)
            {
                conditions.Add("captured_utc >= @from_utc");
                bind += cmd => MysqlCommandHelper.AddDate(cmd, "@from_utc", query.FromUtc.Value);
            }
            if (query.ToUtc.HasValue)
            {
                conditions.Add("captured_utc < @to_utc");
                bind += cmd => MysqlCommandHelper.AddDate(cmd, "@to_utc", query.ToUtc.Value);
            }

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM usage_snapshots" + where + ";", bind, token).ConfigureAwait(false);
                List<UsageSnapshot> rows = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM usage_snapshots" + where + " ORDER BY captured_utc DESC, id DESC" + MysqlCommandHelper.Page(offset, pageSize) + ";",
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

            List<string> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                UsageSnapshotQueries.SnapshotCaptainsSelect,
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@snapshot_id", snapshotId);
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, r => r["captain_id"].ToString()!, token).ConfigureAwait(false);
            return rows;
        }

        /// <inheritdoc />
        public async Task<int> DeleteOlderThanAsync(DateTime capturedBeforeUtc, CancellationToken token = default)
        {
            int deleted = 0;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.SnapshotCaptainsDeleteOlderThan, cmd =>
                {
                    MysqlCommandHelper.AddDate(cmd, "@captured_before", capturedBeforeUtc);
                }, token).ConfigureAwait(false);

                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.SnapshotDeleteOlderThan, cmd =>
                {
                    MysqlCommandHelper.AddDate(cmd, "@captured_before", capturedBeforeUtc);
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

            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.LatestStateUpsert, cmd => BindLatestState(cmd, state), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return state;
        }

        /// <inheritdoc />
        public async Task<UsageLatestState?> ReadLatestStateAsync(string tenantId, string hostId, AgentRuntimeEnum runtime, string accountKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(hostId)) throw new ArgumentNullException(nameof(hostId));
            if (String.IsNullOrEmpty(accountKey)) throw new ArgumentNullException(nameof(accountKey));

            List<UsageLatestState> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                UsageSnapshotQueries.LatestStateSelectByKey,
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@host_id", hostId);
                    MysqlCommandHelper.Add(cmd, "@runtime", runtime.ToString());
                    MysqlCommandHelper.Add(cmd, "@account_key", accountKey);
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
            Action<MySqlCommand> bind = cmd => { };

            if (query.TenantId != null)
            {
                conditions.Add("tenant_id = @tenant_id");
                bind += cmd => MysqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
            }
            if (query.HostId != null)
            {
                conditions.Add("host_id = @host_id");
                bind += cmd => MysqlCommandHelper.Add(cmd, "@host_id", query.HostId);
            }
            if (query.Runtime.HasValue)
            {
                conditions.Add("runtime = @runtime");
                bind += cmd => MysqlCommandHelper.Add(cmd, "@runtime", query.Runtime.Value.ToString());
            }
            if (query.Provider != null)
            {
                conditions.Add("provider = @provider");
                bind += cmd => MysqlCommandHelper.Add(cmd, "@provider", query.Provider);
            }
            if (query.AccountKey != null)
            {
                conditions.Add("account_key = @account_key");
                bind += cmd => MysqlCommandHelper.Add(cmd, "@account_key", query.AccountKey);
            }
            if (!query.IncludeExpired)
            {
                conditions.Add("expired_utc IS NULL");
            }

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM usage_latest_state" + where + ";", bind, token).ConfigureAwait(false);
                List<UsageLatestState> rows = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM usage_latest_state" + where + " ORDER BY account_key ASC, id ASC" + MysqlCommandHelper.Page(offset, pageSize) + ";",
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
        public async Task<int> DeleteByTenantAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));

            int deleted = 0;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.SnapshotCaptainsDeleteByTenant, cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, token).ConfigureAwait(false);

                await MysqlCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.LatestStateDeleteByTenant, cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, token).ConfigureAwait(false);

                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.SnapshotDeleteByTenant, cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void BindLatestState(MySqlCommand cmd, UsageLatestState state)
        {
            MysqlCommandHelper.Add(cmd, "@id", state.Id);
            MysqlCommandHelper.Add(cmd, "@tenant_id", state.TenantId);
            MysqlCommandHelper.Add(cmd, "@host_id", state.HostId);
            MysqlCommandHelper.Add(cmd, "@runtime", state.Runtime.ToString());
            MysqlCommandHelper.Add(cmd, "@provider", String.IsNullOrEmpty(state.Provider) ? null : state.Provider);
            MysqlCommandHelper.Add(cmd, "@account_key", state.AccountKey);
            MysqlCommandHelper.Add(cmd, "@last_good_snapshot_id", String.IsNullOrEmpty(state.LastGoodSnapshotId) ? null : state.LastGoodSnapshotId);
            MysqlCommandHelper.AddDate(cmd, "@last_good_captured_utc", state.LastGoodCapturedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_good_received_utc", state.LastGoodReceivedUtc);
            MysqlCommandHelper.Add(cmd, "@last_good_status", state.LastGoodStatus?.ToString());
            MysqlCommandHelper.Add(cmd, "@last_good_windows_json", state.LastGoodWindowsJson);
            MysqlCommandHelper.Add(cmd, "@last_error_snapshot_id", String.IsNullOrEmpty(state.LastErrorSnapshotId) ? null : state.LastErrorSnapshotId);
            MysqlCommandHelper.AddDate(cmd, "@last_error_captured_utc", state.LastErrorCapturedUtc);
            MysqlCommandHelper.Add(cmd, "@last_error_status", state.LastErrorStatus?.ToString());
            MysqlCommandHelper.Add(cmd, "@last_error_code", state.LastErrorCode);
            MysqlCommandHelper.AddDate(cmd, "@expired_utc", state.ExpiredUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", state.LastUpdateUtc);
        }

        private static UsageSnapshot FromReader(MySqlDataReader reader)
        {
            UsageSnapshot snapshot = new UsageSnapshot();
            snapshot.Id = reader["id"].ToString()!;
            snapshot.TenantId = reader["tenant_id"].ToString()!;
            snapshot.CaptainId = MysqlCommandHelper.ReadString(reader["captain_id"]);
            snapshot.HarborId = MysqlCommandHelper.ReadString(reader["harbor_id"]);
            snapshot.HostId = reader["host_id"].ToString()!;
            snapshot.Runtime = MysqlCommandHelper.ReadEnum(reader["runtime"], AgentRuntimeEnum.ClaudeCode);
            snapshot.Provider = MysqlCommandHelper.ReadString(reader["provider"]);
            snapshot.AccountKey = MysqlCommandHelper.ReadString(reader["account_key"]);
            snapshot.CapturedUtc = MysqlCommandHelper.ReadDate(reader["captured_utc"]);
            snapshot.ReceivedUtc = MysqlCommandHelper.ReadDate(reader["received_utc"]);
            snapshot.Status = MysqlCommandHelper.ReadEnum(reader["status"], UsageSnapshotStatusEnum.Failed);
            snapshot.Source = MysqlCommandHelper.ReadEnum(reader["source"], UsageCaptureSourceEnum.OAuthEndpoint);
            snapshot.WindowsJson = MysqlCommandHelper.ReadString(reader["windows_json"]);
            snapshot.RawExcerpt = MysqlCommandHelper.ReadString(reader["raw_excerpt"]);
            snapshot.ErrorCode = MysqlCommandHelper.ReadString(reader["error_code"]);
            return snapshot;
        }

        private static UsageLatestState LatestStateFromReader(MySqlDataReader reader)
        {
            UsageLatestState state = new UsageLatestState();
            state.Id = reader["id"].ToString()!;
            state.TenantId = reader["tenant_id"].ToString()!;
            state.HostId = reader["host_id"].ToString()!;
            state.Runtime = MysqlCommandHelper.ReadEnum(reader["runtime"], AgentRuntimeEnum.ClaudeCode);
            state.Provider = MysqlCommandHelper.ReadString(reader["provider"]);
            state.AccountKey = reader["account_key"].ToString()!;
            state.LastGoodSnapshotId = MysqlCommandHelper.ReadString(reader["last_good_snapshot_id"]);
            state.LastGoodCapturedUtc = MysqlCommandHelper.ReadNullableDate(reader["last_good_captured_utc"]);
            state.LastGoodReceivedUtc = MysqlCommandHelper.ReadNullableDate(reader["last_good_received_utc"]);
            state.LastGoodStatus = ReadNullableStatus(reader["last_good_status"]);
            state.LastGoodWindowsJson = MysqlCommandHelper.ReadString(reader["last_good_windows_json"]);
            state.LastErrorSnapshotId = MysqlCommandHelper.ReadString(reader["last_error_snapshot_id"]);
            state.LastErrorCapturedUtc = MysqlCommandHelper.ReadNullableDate(reader["last_error_captured_utc"]);
            state.LastErrorStatus = ReadNullableStatus(reader["last_error_status"]);
            state.LastErrorCode = MysqlCommandHelper.ReadString(reader["last_error_code"]);
            state.ExpiredUtc = MysqlCommandHelper.ReadNullableDate(reader["expired_utc"]);
            state.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return state;
        }

        private static UsageSnapshotStatusEnum? ReadNullableStatus(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string? text = MysqlCommandHelper.ReadString(value);
            if (String.IsNullOrEmpty(text)) return null;
            if (Enum.TryParse<UsageSnapshotStatusEnum>(text, true, out UsageSnapshotStatusEnum parsed)) return parsed;
            return null;
        }

        #endregion
    }
}