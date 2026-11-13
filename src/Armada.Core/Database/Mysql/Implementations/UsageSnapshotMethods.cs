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
    using Armada.Core.Services;
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
        public Task<UsageSnapshot> CreateAsync(UsageSnapshot snapshot, List<string>? captainIds, CancellationToken token = default) => CreateWithLatestStateAsync(snapshot, captainIds, null, token);

        /// <inheritdoc />
        public async Task<UsageSnapshot> CreateWithLatestStateAsync(UsageSnapshot snapshot, List<string>? captainIds, UsageLatestState? latestState, CancellationToken token = default)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (String.IsNullOrEmpty(snapshot.TenantId)) throw new ArgumentNullException(nameof(snapshot), "Snapshot tenant id is required.");
            if (String.IsNullOrEmpty(snapshot.HostId)) throw new ArgumentNullException(nameof(snapshot), "Snapshot host id is required.");

            if (latestState != null && (latestState.TenantId != snapshot.TenantId || latestState.HostId != snapshot.HostId || latestState.Runtime != snapshot.Runtime
                || (!String.IsNullOrEmpty(snapshot.AccountKey) && latestState.AccountKey != snapshot.AccountKey)
                || (latestState.LastGoodSnapshotId != snapshot.Id && latestState.LastErrorSnapshotId != snapshot.Id)))
                throw new ArgumentException("The latest state must describe the same tenant, host, account and observation.", nameof(latestState));

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
                    MysqlCommandHelper.Add(cmd, "@probe_started_utc", snapshot.ProbeStartedUtc?.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture));
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
                if (latestState != null)
                {
                    await MysqlCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.LatestStateUpsert, command => BindLatestState(command, latestState), token).ConfigureAwait(false);
                    List<UsageLatestState> saved = await MysqlCommandHelper.QueryAsync(conn, tx, UsageSnapshotQueries.LatestStateSelectByKey, command =>
                    {
                        MysqlCommandHelper.Add(command, "@tenant_id", latestState.TenantId);
                        MysqlCommandHelper.Add(command, "@host_id", latestState.HostId);
                        MysqlCommandHelper.Add(command, "@runtime", latestState.Runtime.ToString());
                        MysqlCommandHelper.Add(command, "@account_key", latestState.AccountKey);
                    }, LatestStateFromReader, token).ConfigureAwait(false);
                    string stateId = saved.Single().Id;
                    await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM usage_latest_state_captains WHERE tenant_id = @tenant_id AND latest_state_id = @state_id;", command =>
                    {
                        MysqlCommandHelper.Add(command, "@tenant_id", latestState.TenantId);
                        MysqlCommandHelper.Add(command, "@state_id", stateId);
                    }, token).ConfigureAwait(false);
                    foreach (string captainId in members)
                    {
                        await MysqlCommandHelper.ExecuteAsync(conn, tx, "INSERT INTO usage_latest_state_captains (tenant_id, latest_state_id, captain_id) VALUES (@tenant_id, @state_id, @captain_id);", command =>
                        {
                            MysqlCommandHelper.Add(command, "@tenant_id", latestState.TenantId);
                            MysqlCommandHelper.Add(command, "@state_id", stateId);
                            MysqlCommandHelper.Add(command, "@captain_id", captainId);
                        }, token).ConfigureAwait(false);
                    }
                }
            }, token).ConfigureAwait(false);

            return snapshot;
        }

        /// <inheritdoc />
        public Task<List<string>> ReadLatestCaptainsAsync(string stateId, string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(stateId)) throw new ArgumentNullException(nameof(stateId));
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return MysqlCommandHelper.QueryAsync(_ConnectionString, "SELECT captain_id FROM usage_latest_state_captains WHERE tenant_id = @tenant_id AND latest_state_id = @state_id ORDER BY captain_id;", command =>
            {
                MysqlCommandHelper.Add(command, "@tenant_id", tenantId);
                MysqlCommandHelper.Add(command, "@state_id", stateId);
            }, reader => reader["captain_id"].ToString()!, token);
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
                List<UsageSnapshot> expired = await MysqlCommandHelper.QueryAsync(conn, tx,
                    "SELECT * FROM usage_snapshots WHERE captured_utc < @captured_before ORDER BY captured_utc, id LIMIT 500 FOR UPDATE;",
                    cmd => MysqlCommandHelper.AddDate(cmd, "@captured_before", capturedBeforeUtc), FromReader, token).ConfigureAwait(false);
                foreach (IGrouping<string, UsageSnapshot> group in expired.Where(row => !String.IsNullOrEmpty(row.AccountKey))
                    .GroupBy(UsageRollupBuilder.GroupKey))
                {
                    UsageSnapshot first = group.First();
                    List<UsageHourlyRollup> existing = await MysqlCommandHelper.QueryAsync(conn, tx,
                        "SELECT * FROM usage_hourly_rollups WHERE tenant_id = @tenant_id AND host_id = @host_id AND runtime = @runtime AND provider = @provider AND account_key = @account_key AND hour_utc = @hour_utc;",
                        cmd =>
                        {
                            MysqlCommandHelper.Add(cmd, "@tenant_id", first.TenantId);
                            MysqlCommandHelper.Add(cmd, "@host_id", first.HostId);
                            MysqlCommandHelper.Add(cmd, "@runtime", first.Runtime.ToString());
                            MysqlCommandHelper.Add(cmd, "@provider", first.Provider ?? String.Empty);
                            MysqlCommandHelper.Add(cmd, "@account_key", first.AccountKey);
                            MysqlCommandHelper.AddDate(cmd, "@hour_utc", UsageRollupBuilder.Hour(first.CapturedUtc));
                        }, UsageHourlyRollupMethods.FromReader, token).ConfigureAwait(false);
                    foreach (UsageHourlyRollup rollup in UsageRollupBuilder.Merge(group.ToList(), existing))
                    {
                        await MysqlCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.RollupUpsert,
                            cmd => UsageHourlyRollupMethods.BindRollup(cmd, rollup), token).ConfigureAwait(false);
                    }
                }

                if (expired.Count == 0) return;
                string ids = String.Join(",", expired.Select((row, index) => "@snapshot_" + index));
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM usage_snapshot_captains WHERE snapshot_id IN (" + ids + ");", cmd =>
                {
                    for (int index = 0; index < expired.Count; index++) MysqlCommandHelper.Add(cmd, "@snapshot_" + index, expired[index].Id);
                }, token).ConfigureAwait(false);
                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM usage_snapshots WHERE id IN (" + ids + ");", cmd =>
                {
                    for (int index = 0; index < expired.Count; index++) MysqlCommandHelper.Add(cmd, "@snapshot_" + index, expired[index].Id);
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
        public async Task<int> PruneInactiveStateAsync(DateTime closedBeforeUtc, CancellationToken token = default)
        {
            int deleted = 0;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                deleted += await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM usage_latest_state WHERE expired_utc < @cutoff;",
                    cmd => MysqlCommandHelper.AddDate(cmd, "@cutoff", closedBeforeUtc), token).ConfigureAwait(false);
                deleted += await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM usage_account_memberships WHERE effective_to_utc < @cutoff;",
                    cmd => MysqlCommandHelper.AddDate(cmd, "@cutoff", closedBeforeUtc), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
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
            MysqlCommandHelper.Add(cmd, "@capture_context_key", state.CaptureContextKey);
            MysqlCommandHelper.Add(cmd, "@requested_interval_seconds", state.RequestedIntervalSeconds);
            MysqlCommandHelper.Add(cmd, "@effective_interval_seconds", state.EffectiveIntervalSeconds);
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
            snapshot.ProbeStartedUtc = reader["probe_started_utc"] == DBNull.Value ? null : DateTime.Parse(reader["probe_started_utc"].ToString()!, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
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
            state.CaptureContextKey = MysqlCommandHelper.ReadString(reader["capture_context_key"]);
            state.RequestedIntervalSeconds = reader["requested_interval_seconds"] == DBNull.Value ? null : MysqlCommandHelper.ReadInt(reader["requested_interval_seconds"], 300);
            state.EffectiveIntervalSeconds = reader["effective_interval_seconds"] == DBNull.Value ? null : MysqlCommandHelper.ReadInt(reader["effective_interval_seconds"], 300);
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