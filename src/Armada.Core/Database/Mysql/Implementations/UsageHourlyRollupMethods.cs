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
    /// MySQL implementation of usage hourly rollup persistence. Upserts are idempotent on the natural key
    /// (tenant, host, runtime, account key, window name, model, hour), so an interrupted rollup job can be
    /// retried without accumulating a second copy of any hour.
    /// </summary>
    public class UsageHourlyRollupMethods : IUsageHourlyRollupMethods
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
        public UsageHourlyRollupMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<UsageHourlyRollup> UpsertAsync(UsageHourlyRollup rollup, CancellationToken token = default)
        {
            if (rollup == null) throw new ArgumentNullException(nameof(rollup));
            if (String.IsNullOrEmpty(rollup.TenantId)) throw new ArgumentNullException(nameof(rollup), "Rollup tenant id is required.");
            if (String.IsNullOrEmpty(rollup.HostId)) throw new ArgumentNullException(nameof(rollup), "Rollup host id is required.");
            if (String.IsNullOrEmpty(rollup.AccountKey)) throw new ArgumentNullException(nameof(rollup), "Rollup account key is required.");
            rollup.LastUpdateUtc = DateTime.UtcNow;

            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.RollupUpsert, cmd => BindRollup(cmd, rollup), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return rollup;
        }

        /// <inheritdoc />
        public async Task<UsageHourlyRollup?> ReadAsync(string id, string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));

            List<UsageHourlyRollup> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                UsageSnapshotQueries.RollupSelectById,
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@id", id);
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<UsageHourlyRollup>> EnumerateAsync(UsageRollupQuery query, CancellationToken token = default)
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
            if (query.WindowName != null)
            {
                conditions.Add("window_name = @window_name");
                bind += cmd => MysqlCommandHelper.Add(cmd, "@window_name", query.WindowName);
            }
            if (query.Model != null)
            {
                conditions.Add("model = @model");
                bind += cmd => MysqlCommandHelper.Add(cmd, "@model", query.Model);
            }
            if (query.FromHourUtc.HasValue)
            {
                conditions.Add("hour_utc >= @from_hour");
                bind += cmd => MysqlCommandHelper.AddDate(cmd, "@from_hour", query.FromHourUtc.Value);
            }
            if (query.ToHourUtc.HasValue)
            {
                conditions.Add("hour_utc < @to_hour");
                bind += cmd => MysqlCommandHelper.AddDate(cmd, "@to_hour", query.ToHourUtc.Value);
            }

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM usage_hourly_rollups" + where + ";", bind, token).ConfigureAwait(false);
                List<UsageHourlyRollup> rows = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM usage_hourly_rollups" + where + " ORDER BY hour_utc DESC, id DESC" + MysqlCommandHelper.Page(offset, pageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);

                EnumerationResult<UsageHourlyRollup> result = new EnumerationResult<UsageHourlyRollup>();
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalRecords = total;
                result.TotalPages = (int)Math.Ceiling((double)total / pageSize);
                result.Objects = rows;
                return result;
            }
        }

        /// <inheritdoc />
        public async Task<int> DeleteOlderThanAsync(DateTime hourBeforeUtc, CancellationToken token = default)
        {
            int deleted = 0;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.RollupDeleteOlderThan, cmd =>
                {
                    MysqlCommandHelper.AddDate(cmd, "@hour_before", hourBeforeUtc);
                }, token).ConfigureAwait(false);
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
                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.RollupDeleteByTenant, cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static string? NullIfEmpty(string? value)
        {
            return String.IsNullOrEmpty(value) ? null : value;
        }

        internal static void BindRollup(MySqlCommand cmd, UsageHourlyRollup rollup)
        {
            MysqlCommandHelper.Add(cmd, "@id", rollup.Id);
            MysqlCommandHelper.Add(cmd, "@tenant_id", rollup.TenantId);
            MysqlCommandHelper.Add(cmd, "@host_id", rollup.HostId);
            MysqlCommandHelper.Add(cmd, "@runtime", rollup.Runtime.ToString());
            MysqlCommandHelper.Add(cmd, "@provider", rollup.Provider ?? String.Empty);
            MysqlCommandHelper.Add(cmd, "@account_key", rollup.AccountKey);
            MysqlCommandHelper.Add(cmd, "@window_name", rollup.WindowName ?? String.Empty);
            MysqlCommandHelper.Add(cmd, "@model", rollup.Model ?? String.Empty);
            MysqlCommandHelper.AddDate(cmd, "@hour_utc", rollup.HourUtc);
            MysqlCommandHelper.Add(cmd, "@sample_count", rollup.SampleCount);
            MysqlCommandHelper.Add(cmd, "@percent_sample_count", rollup.PercentSampleCount);
            MysqlCommandHelper.Add(cmd, "@last_status", rollup.LastStatus);
            MysqlCommandHelper.Add(cmd, "@raw_pruned", rollup.RawPruned ? 1 : 0);
            MysqlCommandHelper.Add(cmd, "@last_percent", rollup.LastPercent);
            MysqlCommandHelper.Add(cmd, "@min_percent", rollup.MinPercent);
            MysqlCommandHelper.Add(cmd, "@max_percent", rollup.MaxPercent);
            MysqlCommandHelper.Add(cmd, "@mean_percent", rollup.MeanPercent);
            MysqlCommandHelper.Add(cmd, "@last_used", rollup.LastUsed);
            MysqlCommandHelper.Add(cmd, "@last_limit", rollup.LastLimit);
            MysqlCommandHelper.AddDate(cmd, "@last_reset_utc", rollup.LastResetUtc);
            MysqlCommandHelper.AddDate(cmd, "@first_observed_utc", rollup.FirstObservedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_observed_utc", rollup.LastObservedUtc);
            MysqlCommandHelper.Add(cmd, "@ok_count", rollup.OkCount);
            MysqlCommandHelper.Add(cmd, "@unsupported_count", rollup.UnsupportedCount);
            MysqlCommandHelper.Add(cmd, "@auth_missing_count", rollup.AuthMissingCount);
            MysqlCommandHelper.Add(cmd, "@failed_count", rollup.FailedCount);
            MysqlCommandHelper.Add(cmd, "@estimated_count", rollup.EstimatedCount);
            MysqlCommandHelper.Add(cmd, "@coverage_seconds", rollup.CoverageSeconds);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", rollup.LastUpdateUtc);
        }

        internal static UsageHourlyRollup FromReader(MySqlDataReader reader)
        {
            UsageHourlyRollup rollup = new UsageHourlyRollup();
            rollup.Id = reader["id"].ToString()!;
            rollup.TenantId = reader["tenant_id"].ToString()!;
            rollup.HostId = reader["host_id"].ToString()!;
            rollup.Runtime = MysqlCommandHelper.ReadEnum(reader["runtime"], AgentRuntimeEnum.ClaudeCode);
            rollup.Provider = NullIfEmpty(MysqlCommandHelper.ReadString(reader["provider"]));
            rollup.AccountKey = reader["account_key"].ToString()!;
            rollup.WindowName = MysqlCommandHelper.ReadString(reader["window_name"]) ?? String.Empty;
            rollup.Model = MysqlCommandHelper.ReadString(reader["model"]) ?? String.Empty;
            rollup.HourUtc = MysqlCommandHelper.ReadDate(reader["hour_utc"]);
            rollup.SampleCount = MysqlCommandHelper.ReadInt(reader["sample_count"], 0);
            rollup.LastPercent = MysqlCommandHelper.ReadNullableDouble(reader["last_percent"]);
            rollup.MinPercent = MysqlCommandHelper.ReadNullableDouble(reader["min_percent"]);
            rollup.MaxPercent = MysqlCommandHelper.ReadNullableDouble(reader["max_percent"]);
            rollup.MeanPercent = MysqlCommandHelper.ReadNullableDouble(reader["mean_percent"]);
            rollup.LastUsed = MysqlCommandHelper.ReadNullableLong(reader["last_used"]);
            rollup.LastLimit = MysqlCommandHelper.ReadNullableLong(reader["last_limit"]);
            rollup.LastResetUtc = MysqlCommandHelper.ReadNullableDate(reader["last_reset_utc"]);
            rollup.FirstObservedUtc = MysqlCommandHelper.ReadNullableDate(reader["first_observed_utc"]);
            rollup.LastObservedUtc = MysqlCommandHelper.ReadNullableDate(reader["last_observed_utc"]);
            rollup.OkCount = MysqlCommandHelper.ReadInt(reader["ok_count"], 0);
            rollup.UnsupportedCount = MysqlCommandHelper.ReadInt(reader["unsupported_count"], 0);
            rollup.AuthMissingCount = MysqlCommandHelper.ReadInt(reader["auth_missing_count"], 0);
            rollup.FailedCount = MysqlCommandHelper.ReadInt(reader["failed_count"], 0);
            rollup.EstimatedCount = MysqlCommandHelper.ReadInt(reader["estimated_count"], 0);
            rollup.CoverageSeconds = MysqlCommandHelper.ReadNullableDouble(reader["coverage_seconds"]);
            rollup.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            rollup.PercentSampleCount = MysqlCommandHelper.ReadInt(reader["percent_sample_count"], 0);
            rollup.LastStatus = MysqlCommandHelper.ReadString(reader["last_status"]);
            rollup.RawPruned = MysqlCommandHelper.ReadInt(reader["raw_pruned"], 0) != 0;
            return rollup;
        }

        #endregion
    }
}