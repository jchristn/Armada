namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Database.Sqlite.Queries;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of usage snapshot persistence: snapshots, their captain member links and the
    /// per-account latest state. A snapshot and its links are written in one transaction; latest state is
    /// keyed by tenant, host, runtime and account key and upserted so a failed capture can carry the error
    /// without erasing the last good summary.
    /// </summary>
    public class UsageSnapshotMethods : IUsageSnapshotMethods
    {
        #region Private-Members

        private readonly string _ConnectionString;
        private readonly SqliteWriteGate _WriteGate;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQLite database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public UsageSnapshotMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _ConnectionString = driver.ConnectionString;
            _WriteGate = driver.WriteGate;
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

            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.SnapshotInsert, cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@id", snapshot.Id);
                    SqliteCommandHelper.Add(cmd, "@tenant_id", snapshot.TenantId);
                    SqliteCommandHelper.Add(cmd, "@captain_id", String.IsNullOrEmpty(snapshot.CaptainId) ? null : snapshot.CaptainId);
                    SqliteCommandHelper.Add(cmd, "@harbor_id", String.IsNullOrEmpty(snapshot.HarborId) ? null : snapshot.HarborId);
                    SqliteCommandHelper.Add(cmd, "@host_id", snapshot.HostId);
                    SqliteCommandHelper.Add(cmd, "@runtime", snapshot.Runtime.ToString());
                    SqliteCommandHelper.Add(cmd, "@provider", String.IsNullOrEmpty(snapshot.Provider) ? null : snapshot.Provider);
                    SqliteCommandHelper.Add(cmd, "@account_key", String.IsNullOrEmpty(snapshot.AccountKey) ? null : snapshot.AccountKey);
                    SqliteCommandHelper.AddDate(cmd, "@captured_utc", snapshot.CapturedUtc);
                    SqliteCommandHelper.AddDate(cmd, "@received_utc", snapshot.ReceivedUtc);
                    SqliteCommandHelper.Add(cmd, "@status", snapshot.Status.ToString());
                    SqliteCommandHelper.Add(cmd, "@source", snapshot.Source.ToString());
                    SqliteCommandHelper.Add(cmd, "@windows_json", snapshot.WindowsJson);
                    SqliteCommandHelper.Add(cmd, "@raw_excerpt", snapshot.RawExcerpt);
                    SqliteCommandHelper.Add(cmd, "@error_code", String.IsNullOrEmpty(snapshot.ErrorCode) ? null : snapshot.ErrorCode);
                }, token).ConfigureAwait(false);

                foreach (string captainId in members)
                {
                    string linkId = Constants.IdGenerator.GenerateKSortable(Constants.UsageSnapshotCaptainIdPrefix, 24);
                    await SqliteCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.SnapshotCaptainsInsert, cmd =>
                    {
                        SqliteCommandHelper.Add(cmd, "@id", linkId);
                        SqliteCommandHelper.Add(cmd, "@tenant_id", snapshot.TenantId);
                        SqliteCommandHelper.Add(cmd, "@snapshot_id", snapshot.Id);
                        SqliteCommandHelper.Add(cmd, "@captain_id", captainId);
                        SqliteCommandHelper.AddDate(cmd, "@captured_utc", snapshot.CapturedUtc);
                    }, token).ConfigureAwait(false);
                }
                if (latestState != null)
                {
                    await SqliteCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.LatestStateUpsert, command => BindLatestState(command, latestState), token).ConfigureAwait(false);
                    List<UsageLatestState> saved = await SqliteCommandHelper.QueryAsync(conn, tx, UsageSnapshotQueries.LatestStateSelectByKey, command =>
                    {
                        SqliteCommandHelper.Add(command, "@tenant_id", latestState.TenantId);
                        SqliteCommandHelper.Add(command, "@host_id", latestState.HostId);
                        SqliteCommandHelper.Add(command, "@runtime", latestState.Runtime.ToString());
                        SqliteCommandHelper.Add(command, "@account_key", latestState.AccountKey);
                    }, LatestStateFromReader, token).ConfigureAwait(false);
                    string stateId = saved.Single().Id;
                    await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM usage_latest_state_captains WHERE tenant_id = @tenant_id AND latest_state_id = @state_id;", command =>
                    {
                        SqliteCommandHelper.Add(command, "@tenant_id", latestState.TenantId);
                        SqliteCommandHelper.Add(command, "@state_id", stateId);
                    }, token).ConfigureAwait(false);
                    foreach (string captainId in members)
                    {
                        await SqliteCommandHelper.ExecuteAsync(conn, tx, "INSERT INTO usage_latest_state_captains (tenant_id, latest_state_id, captain_id) VALUES (@tenant_id, @state_id, @captain_id);", command =>
                        {
                            SqliteCommandHelper.Add(command, "@tenant_id", latestState.TenantId);
                            SqliteCommandHelper.Add(command, "@state_id", stateId);
                            SqliteCommandHelper.Add(command, "@captain_id", captainId);
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
            return SqliteCommandHelper.QueryAsync(_ConnectionString, "SELECT captain_id FROM usage_latest_state_captains WHERE tenant_id = @tenant_id AND latest_state_id = @state_id ORDER BY captain_id;", command =>
            {
                SqliteCommandHelper.Add(command, "@tenant_id", tenantId);
                SqliteCommandHelper.Add(command, "@state_id", stateId);
            }, reader => reader["captain_id"].ToString()!, token);
        }

        /// <inheritdoc />
        public async Task<UsageSnapshot?> ReadAsync(string id, string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));

            List<UsageSnapshot> rows = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                UsageSnapshotQueries.SnapshotSelectById,
                cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@id", id);
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
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
            Action<SqliteCommand> bind = cmd => { };

            if (query.TenantId != null)
            {
                conditions.Add("tenant_id = @tenant_id");
                bind += cmd => SqliteCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
            }
            if (query.CaptainId != null)
            {
                conditions.Add("(captain_id = @captain_id OR EXISTS (SELECT 1 FROM usage_snapshot_captains l WHERE l.snapshot_id = usage_snapshots.id AND l.captain_id = @captain_id))");
                bind += cmd => SqliteCommandHelper.Add(cmd, "@captain_id", query.CaptainId);
            }
            if (query.HarborId != null)
            {
                conditions.Add("harbor_id = @harbor_id");
                bind += cmd => SqliteCommandHelper.Add(cmd, "@harbor_id", query.HarborId);
            }
            if (query.Runtime.HasValue)
            {
                conditions.Add("runtime = @runtime");
                bind += cmd => SqliteCommandHelper.Add(cmd, "@runtime", query.Runtime.Value.ToString());
            }
            if (query.Provider != null)
            {
                conditions.Add("provider = @provider");
                bind += cmd => SqliteCommandHelper.Add(cmd, "@provider", query.Provider);
            }
            if (query.AccountKey != null)
            {
                conditions.Add("account_key = @account_key");
                bind += cmd => SqliteCommandHelper.Add(cmd, "@account_key", query.AccountKey);
            }
            if (query.Status.HasValue)
            {
                conditions.Add("status = @status");
                bind += cmd => SqliteCommandHelper.Add(cmd, "@status", query.Status.Value.ToString());
            }
            if (query.FromUtc.HasValue)
            {
                conditions.Add("captured_utc >= @from_utc");
                bind += cmd => SqliteCommandHelper.AddDate(cmd, "@from_utc", query.FromUtc.Value);
            }
            if (query.ToUtc.HasValue)
            {
                conditions.Add("captured_utc < @to_utc");
                bind += cmd => SqliteCommandHelper.AddDate(cmd, "@to_utc", query.ToUtc.Value);
            }

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (SqliteConnection conn = new SqliteProviderConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqliteCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM usage_snapshots" + where + ";", bind, token).ConfigureAwait(false);
                List<UsageSnapshot> rows = await SqliteCommandHelper.QueryAsync(conn,
                    "SELECT * FROM usage_snapshots" + where + " ORDER BY captured_utc DESC, id DESC" + SqliteCommandHelper.Page(offset, pageSize) + ";",
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

            List<string> rows = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                UsageSnapshotQueries.SnapshotCaptainsSelect,
                cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@snapshot_id", snapshotId);
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, r => r["captain_id"].ToString()!, token).ConfigureAwait(false);
            return rows;
        }

        /// <inheritdoc />
        public async Task<int> DeleteOlderThanAsync(DateTime capturedBeforeUtc, CancellationToken token = default)
        {
            int deleted = 0;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                List<UsageSnapshot> expired = await SqliteCommandHelper.QueryAsync(conn, tx,
                    "SELECT * FROM usage_snapshots WHERE captured_utc < @captured_before ORDER BY captured_utc, id LIMIT 500;",
                    cmd => SqliteCommandHelper.AddDate(cmd, "@captured_before", capturedBeforeUtc), FromReader, token).ConfigureAwait(false);
                foreach (IGrouping<string, UsageSnapshot> group in expired.Where(row => !String.IsNullOrEmpty(row.AccountKey))
                    .GroupBy(UsageRollupBuilder.GroupKey))
                {
                    UsageSnapshot first = group.First();
                    List<UsageHourlyRollup> existing = await SqliteCommandHelper.QueryAsync(conn, tx,
                        "SELECT * FROM usage_hourly_rollups WHERE tenant_id = @tenant_id AND host_id = @host_id AND runtime = @runtime AND provider = @provider AND account_key = @account_key AND hour_utc = @hour_utc;",
                        cmd =>
                        {
                            SqliteCommandHelper.Add(cmd, "@tenant_id", first.TenantId);
                            SqliteCommandHelper.Add(cmd, "@host_id", first.HostId);
                            SqliteCommandHelper.Add(cmd, "@runtime", first.Runtime.ToString());
                            SqliteCommandHelper.Add(cmd, "@provider", first.Provider ?? String.Empty);
                            SqliteCommandHelper.Add(cmd, "@account_key", first.AccountKey);
                            SqliteCommandHelper.AddDate(cmd, "@hour_utc", UsageRollupBuilder.Hour(first.CapturedUtc));
                        }, UsageHourlyRollupMethods.FromReader, token).ConfigureAwait(false);
                    foreach (UsageHourlyRollup rollup in UsageRollupBuilder.Merge(group.ToList(), existing))
                    {
                        await SqliteCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.RollupUpsert,
                            cmd => UsageHourlyRollupMethods.BindRollup(cmd, rollup), token).ConfigureAwait(false);
                    }
                }

                if (expired.Count == 0) return;
                string ids = String.Join(",", expired.Select((row, index) => "@snapshot_" + index));
                await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM usage_snapshot_captains WHERE snapshot_id IN (" + ids + ");", cmd =>
                {
                    for (int index = 0; index < expired.Count; index++) SqliteCommandHelper.Add(cmd, "@snapshot_" + index, expired[index].Id);
                }, token).ConfigureAwait(false);
                deleted = await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM usage_snapshots WHERE id IN (" + ids + ");", cmd =>
                {
                    for (int index = 0; index < expired.Count; index++) SqliteCommandHelper.Add(cmd, "@snapshot_" + index, expired[index].Id);
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

            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.LatestStateUpsert, cmd => BindLatestState(cmd, state), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return state;
        }

        /// <inheritdoc />
        public async Task<UsageLatestState?> ReadLatestStateAsync(string tenantId, string hostId, AgentRuntimeEnum runtime, string accountKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(hostId)) throw new ArgumentNullException(nameof(hostId));
            if (String.IsNullOrEmpty(accountKey)) throw new ArgumentNullException(nameof(accountKey));

            List<UsageLatestState> rows = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                UsageSnapshotQueries.LatestStateSelectByKey,
                cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqliteCommandHelper.Add(cmd, "@host_id", hostId);
                    SqliteCommandHelper.Add(cmd, "@runtime", runtime.ToString());
                    SqliteCommandHelper.Add(cmd, "@account_key", accountKey);
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
            Action<SqliteCommand> bind = cmd => { };

            if (query.TenantId != null)
            {
                conditions.Add("tenant_id = @tenant_id");
                bind += cmd => SqliteCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
            }
            if (query.HostId != null)
            {
                conditions.Add("host_id = @host_id");
                bind += cmd => SqliteCommandHelper.Add(cmd, "@host_id", query.HostId);
            }
            if (query.Runtime.HasValue)
            {
                conditions.Add("runtime = @runtime");
                bind += cmd => SqliteCommandHelper.Add(cmd, "@runtime", query.Runtime.Value.ToString());
            }
            if (query.Provider != null)
            {
                conditions.Add("provider = @provider");
                bind += cmd => SqliteCommandHelper.Add(cmd, "@provider", query.Provider);
            }
            if (query.AccountKey != null)
            {
                conditions.Add("account_key = @account_key");
                bind += cmd => SqliteCommandHelper.Add(cmd, "@account_key", query.AccountKey);
            }
            if (!query.IncludeExpired)
            {
                conditions.Add("expired_utc IS NULL");
            }

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (SqliteConnection conn = new SqliteProviderConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqliteCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM usage_latest_state" + where + ";", bind, token).ConfigureAwait(false);
                List<UsageLatestState> rows = await SqliteCommandHelper.QueryAsync(conn,
                    "SELECT * FROM usage_latest_state" + where + " ORDER BY account_key ASC, id ASC" + SqliteCommandHelper.Page(offset, pageSize) + ";",
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
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                deleted += await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM usage_latest_state WHERE expired_utc < @cutoff;",
                    cmd => SqliteCommandHelper.AddDate(cmd, "@cutoff", closedBeforeUtc), token).ConfigureAwait(false);
                deleted += await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM usage_account_memberships WHERE effective_to_utc < @cutoff;",
                    cmd => SqliteCommandHelper.AddDate(cmd, "@cutoff", closedBeforeUtc), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        /// <inheritdoc />
        public async Task<int> DeleteByTenantAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));

            int deleted = 0;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.SnapshotCaptainsDeleteByTenant, cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, token).ConfigureAwait(false);

                await SqliteCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.LatestStateDeleteByTenant, cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, token).ConfigureAwait(false);

                deleted = await SqliteCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.SnapshotDeleteByTenant, cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void BindLatestState(SqliteCommand cmd, UsageLatestState state)
        {
            SqliteCommandHelper.Add(cmd, "@id", state.Id);
            SqliteCommandHelper.Add(cmd, "@tenant_id", state.TenantId);
            SqliteCommandHelper.Add(cmd, "@host_id", state.HostId);
            SqliteCommandHelper.Add(cmd, "@runtime", state.Runtime.ToString());
            SqliteCommandHelper.Add(cmd, "@provider", String.IsNullOrEmpty(state.Provider) ? null : state.Provider);
            SqliteCommandHelper.Add(cmd, "@account_key", state.AccountKey);
            SqliteCommandHelper.Add(cmd, "@capture_context_key", state.CaptureContextKey);
            SqliteCommandHelper.Add(cmd, "@requested_interval_seconds", state.RequestedIntervalSeconds);
            SqliteCommandHelper.Add(cmd, "@effective_interval_seconds", state.EffectiveIntervalSeconds);
            SqliteCommandHelper.Add(cmd, "@last_good_snapshot_id", String.IsNullOrEmpty(state.LastGoodSnapshotId) ? null : state.LastGoodSnapshotId);
            SqliteCommandHelper.AddDate(cmd, "@last_good_captured_utc", state.LastGoodCapturedUtc);
            SqliteCommandHelper.AddDate(cmd, "@last_good_received_utc", state.LastGoodReceivedUtc);
            SqliteCommandHelper.Add(cmd, "@last_good_status", state.LastGoodStatus?.ToString());
            SqliteCommandHelper.Add(cmd, "@last_good_windows_json", state.LastGoodWindowsJson);
            SqliteCommandHelper.Add(cmd, "@last_error_snapshot_id", String.IsNullOrEmpty(state.LastErrorSnapshotId) ? null : state.LastErrorSnapshotId);
            SqliteCommandHelper.AddDate(cmd, "@last_error_captured_utc", state.LastErrorCapturedUtc);
            SqliteCommandHelper.Add(cmd, "@last_error_status", state.LastErrorStatus?.ToString());
            SqliteCommandHelper.Add(cmd, "@last_error_code", state.LastErrorCode);
            SqliteCommandHelper.AddDate(cmd, "@expired_utc", state.ExpiredUtc);
            SqliteCommandHelper.AddDate(cmd, "@last_update_utc", state.LastUpdateUtc);
        }

        private static UsageSnapshot FromReader(SqliteDataReader reader)
        {
            UsageSnapshot snapshot = new UsageSnapshot();
            snapshot.Id = reader["id"].ToString()!;
            snapshot.TenantId = reader["tenant_id"].ToString()!;
            snapshot.CaptainId = SqliteCommandHelper.ReadString(reader["captain_id"]);
            snapshot.HarborId = SqliteCommandHelper.ReadString(reader["harbor_id"]);
            snapshot.HostId = reader["host_id"].ToString()!;
            snapshot.Runtime = SqliteCommandHelper.ReadEnum(reader["runtime"], AgentRuntimeEnum.ClaudeCode);
            snapshot.Provider = SqliteCommandHelper.ReadString(reader["provider"]);
            snapshot.AccountKey = SqliteCommandHelper.ReadString(reader["account_key"]);
            snapshot.CapturedUtc = SqliteCommandHelper.ReadDate(reader["captured_utc"]);
            snapshot.ReceivedUtc = SqliteCommandHelper.ReadDate(reader["received_utc"]);
            snapshot.Status = SqliteCommandHelper.ReadEnum(reader["status"], UsageSnapshotStatusEnum.Failed);
            snapshot.Source = SqliteCommandHelper.ReadEnum(reader["source"], UsageCaptureSourceEnum.OAuthEndpoint);
            snapshot.WindowsJson = SqliteCommandHelper.ReadString(reader["windows_json"]);
            snapshot.RawExcerpt = SqliteCommandHelper.ReadString(reader["raw_excerpt"]);
            snapshot.ErrorCode = SqliteCommandHelper.ReadString(reader["error_code"]);
            return snapshot;
        }

        private static UsageLatestState LatestStateFromReader(SqliteDataReader reader)
        {
            UsageLatestState state = new UsageLatestState();
            state.Id = reader["id"].ToString()!;
            state.TenantId = reader["tenant_id"].ToString()!;
            state.HostId = reader["host_id"].ToString()!;
            state.Runtime = SqliteCommandHelper.ReadEnum(reader["runtime"], AgentRuntimeEnum.ClaudeCode);
            state.Provider = SqliteCommandHelper.ReadString(reader["provider"]);
            state.CaptureContextKey = SqliteCommandHelper.ReadString(reader["capture_context_key"]);
            state.RequestedIntervalSeconds = reader["requested_interval_seconds"] == DBNull.Value ? null : SqliteCommandHelper.ReadInt(reader["requested_interval_seconds"], 300);
            state.EffectiveIntervalSeconds = reader["effective_interval_seconds"] == DBNull.Value ? null : SqliteCommandHelper.ReadInt(reader["effective_interval_seconds"], 300);
            state.AccountKey = reader["account_key"].ToString()!;
            state.LastGoodSnapshotId = SqliteCommandHelper.ReadString(reader["last_good_snapshot_id"]);
            state.LastGoodCapturedUtc = SqliteCommandHelper.ReadNullableDate(reader["last_good_captured_utc"]);
            state.LastGoodReceivedUtc = SqliteCommandHelper.ReadNullableDate(reader["last_good_received_utc"]);
            state.LastGoodStatus = ReadNullableStatus(reader["last_good_status"]);
            state.LastGoodWindowsJson = SqliteCommandHelper.ReadString(reader["last_good_windows_json"]);
            state.LastErrorSnapshotId = SqliteCommandHelper.ReadString(reader["last_error_snapshot_id"]);
            state.LastErrorCapturedUtc = SqliteCommandHelper.ReadNullableDate(reader["last_error_captured_utc"]);
            state.LastErrorStatus = ReadNullableStatus(reader["last_error_status"]);
            state.LastErrorCode = SqliteCommandHelper.ReadString(reader["last_error_code"]);
            state.ExpiredUtc = SqliteCommandHelper.ReadNullableDate(reader["expired_utc"]);
            state.LastUpdateUtc = SqliteCommandHelper.ReadDate(reader["last_update_utc"]);
            return state;
        }

        private static UsageSnapshotStatusEnum? ReadNullableStatus(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string? text = SqliteCommandHelper.ReadString(value);
            if (String.IsNullOrEmpty(text)) return null;
            if (Enum.TryParse<UsageSnapshotStatusEnum>(text, true, out UsageSnapshotStatusEnum parsed)) return parsed;
            return null;
        }

        #endregion
    }
}