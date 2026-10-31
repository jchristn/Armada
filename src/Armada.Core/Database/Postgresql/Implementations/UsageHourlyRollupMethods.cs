namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Database.Postgresql.Queries;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// PostgreSQL implementation of usage hourly rollup persistence. Upserts are idempotent on the natural key
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
        /// <param name="driver">PostgreSQL database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public UsageHourlyRollupMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<UsageHourlyRollup> UpsertAsync(UsageHourlyRollup rollup, CancellationToken token = default)
        {
            if (rollup == null) throw new ArgumentNullException(nameof(rollup));
            if (String.IsNullOrEmpty(rollup.TenantId)) throw new ArgumentNullException(nameof(rollup), "Rollup tenant id is required.");
            if (String.IsNullOrEmpty(rollup.HostId)) throw new ArgumentNullException(nameof(rollup), "Rollup host id is required.");
            if (String.IsNullOrEmpty(rollup.AccountKey)) throw new ArgumentNullException(nameof(rollup), "Rollup account key is required.");
            rollup.LastUpdateUtc = DateTime.UtcNow;

            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.RollupUpsert, cmd => BindRollup(cmd, rollup), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return rollup;
        }

        /// <inheritdoc />
        public async Task<UsageHourlyRollup?> ReadAsync(string id, string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));

            List<UsageHourlyRollup> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                UsageSnapshotQueries.RollupSelectById,
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@id", id);
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
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
            Action<NpgsqlCommand> bind = cmd => { };

            if (query.TenantId != null)
            {
                conditions.Add("tenant_id = @tenant_id");
                bind += cmd => PostgresqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
            }
            if (query.HostId != null)
            {
                conditions.Add("host_id = @host_id");
                bind += cmd => PostgresqlCommandHelper.Add(cmd, "@host_id", query.HostId);
            }
            if (query.Runtime.HasValue)
            {
                conditions.Add("runtime = @runtime");
                bind += cmd => PostgresqlCommandHelper.Add(cmd, "@runtime", query.Runtime.Value.ToString());
            }
            if (query.Provider != null)
            {
                conditions.Add("provider = @provider");
                bind += cmd => PostgresqlCommandHelper.Add(cmd, "@provider", query.Provider);
            }
            if (query.AccountKey != null)
            {
                conditions.Add("account_key = @account_key");
                bind += cmd => PostgresqlCommandHelper.Add(cmd, "@account_key", query.AccountKey);
            }
            if (query.WindowName != null)
            {
                conditions.Add("window_name = @window_name");
                bind += cmd => PostgresqlCommandHelper.Add(cmd, "@window_name", query.WindowName);
            }
            if (query.Model != null)
            {
                conditions.Add("model = @model");
                bind += cmd => PostgresqlCommandHelper.Add(cmd, "@model", query.Model);
            }
            if (query.FromHourUtc.HasValue)
            {
                conditions.Add("hour_utc >= @from_hour");
                bind += cmd => PostgresqlCommandHelper.AddDate(cmd, "@from_hour", query.FromHourUtc.Value);
            }
            if (query.ToHourUtc.HasValue)
            {
                conditions.Add("hour_utc < @to_hour");
                bind += cmd => PostgresqlCommandHelper.AddDate(cmd, "@to_hour", query.ToHourUtc.Value);
            }

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (NpgsqlConnection conn = new NpgsqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await PostgresqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM usage_hourly_rollups" + where + ";", bind, token).ConfigureAwait(false);
                List<UsageHourlyRollup> rows = await PostgresqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM usage_hourly_rollups" + where + " ORDER BY hour_utc DESC, id DESC" + PostgresqlCommandHelper.Page(offset, pageSize) + ";",
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
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                deleted = await PostgresqlCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.RollupDeleteOlderThan, cmd =>
                {
                    PostgresqlCommandHelper.AddDate(cmd, "@hour_before", hourBeforeUtc);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        /// <inheritdoc />
        public async Task<int> DeleteByTenantAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));

            int deleted = 0;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                deleted = await PostgresqlCommandHelper.ExecuteAsync(conn, tx, UsageSnapshotQueries.RollupDeleteByTenant, cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void BindRollup(NpgsqlCommand cmd, UsageHourlyRollup rollup)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", rollup.Id);
            PostgresqlCommandHelper.Add(cmd, "@tenant_id", rollup.TenantId);
            PostgresqlCommandHelper.Add(cmd, "@host_id", rollup.HostId);
            PostgresqlCommandHelper.Add(cmd, "@runtime", rollup.Runtime.ToString());
            PostgresqlCommandHelper.Add(cmd, "@provider", String.IsNullOrEmpty(rollup.Provider) ? null : rollup.Provider);
            PostgresqlCommandHelper.Add(cmd, "@account_key", rollup.AccountKey);
            PostgresqlCommandHelper.Add(cmd, "@window_name", rollup.WindowName ?? String.Empty);
            PostgresqlCommandHelper.Add(cmd, "@model", rollup.Model ?? String.Empty);
            PostgresqlCommandHelper.AddDate(cmd, "@hour_utc", rollup.HourUtc);
            PostgresqlCommandHelper.Add(cmd, "@sample_count", rollup.SampleCount);
            PostgresqlCommandHelper.Add(cmd, "@last_percent", rollup.LastPercent);
            PostgresqlCommandHelper.Add(cmd, "@min_percent", rollup.MinPercent);
            PostgresqlCommandHelper.Add(cmd, "@max_percent", rollup.MaxPercent);
            PostgresqlCommandHelper.Add(cmd, "@mean_percent", rollup.MeanPercent);
            PostgresqlCommandHelper.Add(cmd, "@last_used", rollup.LastUsed);
            PostgresqlCommandHelper.Add(cmd, "@last_limit", rollup.LastLimit);
            PostgresqlCommandHelper.AddDate(cmd, "@last_reset_utc", rollup.LastResetUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@first_observed_utc", rollup.FirstObservedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@last_observed_utc", rollup.LastObservedUtc);
            PostgresqlCommandHelper.Add(cmd, "@ok_count", rollup.OkCount);
            PostgresqlCommandHelper.Add(cmd, "@unsupported_count", rollup.UnsupportedCount);
            PostgresqlCommandHelper.Add(cmd, "@auth_missing_count", rollup.AuthMissingCount);
            PostgresqlCommandHelper.Add(cmd, "@failed_count", rollup.FailedCount);
            PostgresqlCommandHelper.Add(cmd, "@estimated_count", rollup.EstimatedCount);
            PostgresqlCommandHelper.Add(cmd, "@coverage_seconds", rollup.CoverageSeconds);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", rollup.LastUpdateUtc);
        }

        private static UsageHourlyRollup FromReader(NpgsqlDataReader reader)
        {
            UsageHourlyRollup rollup = new UsageHourlyRollup();
            rollup.Id = reader["id"].ToString()!;
            rollup.TenantId = reader["tenant_id"].ToString()!;
            rollup.HostId = reader["host_id"].ToString()!;
            rollup.Runtime = PostgresqlCommandHelper.ReadEnum(reader["runtime"], AgentRuntimeEnum.ClaudeCode);
            rollup.Provider = PostgresqlCommandHelper.ReadString(reader["provider"]);
            rollup.AccountKey = reader["account_key"].ToString()!;
            rollup.WindowName = PostgresqlCommandHelper.ReadString(reader["window_name"]) ?? String.Empty;
            rollup.Model = PostgresqlCommandHelper.ReadString(reader["model"]) ?? String.Empty;
            rollup.HourUtc = PostgresqlCommandHelper.ReadDate(reader["hour_utc"]);
            rollup.SampleCount = PostgresqlCommandHelper.ReadInt(reader["sample_count"], 0);
            rollup.LastPercent = PostgresqlCommandHelper.ReadNullableDouble(reader["last_percent"]);
            rollup.MinPercent = PostgresqlCommandHelper.ReadNullableDouble(reader["min_percent"]);
            rollup.MaxPercent = PostgresqlCommandHelper.ReadNullableDouble(reader["max_percent"]);
            rollup.MeanPercent = PostgresqlCommandHelper.ReadNullableDouble(reader["mean_percent"]);
            rollup.LastUsed = PostgresqlCommandHelper.ReadNullableLong(reader["last_used"]);
            rollup.LastLimit = PostgresqlCommandHelper.ReadNullableLong(reader["last_limit"]);
            rollup.LastResetUtc = PostgresqlCommandHelper.ReadNullableDate(reader["last_reset_utc"]);
            rollup.FirstObservedUtc = PostgresqlCommandHelper.ReadNullableDate(reader["first_observed_utc"]);
            rollup.LastObservedUtc = PostgresqlCommandHelper.ReadNullableDate(reader["last_observed_utc"]);
            rollup.OkCount = PostgresqlCommandHelper.ReadInt(reader["ok_count"], 0);
            rollup.UnsupportedCount = PostgresqlCommandHelper.ReadInt(reader["unsupported_count"], 0);
            rollup.AuthMissingCount = PostgresqlCommandHelper.ReadInt(reader["auth_missing_count"], 0);
            rollup.FailedCount = PostgresqlCommandHelper.ReadInt(reader["failed_count"], 0);
            rollup.EstimatedCount = PostgresqlCommandHelper.ReadInt(reader["estimated_count"], 0);
            rollup.CoverageSeconds = PostgresqlCommandHelper.ReadNullableDouble(reader["coverage_seconds"]);
            rollup.LastUpdateUtc = PostgresqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return rollup;
        }

        #endregion
    }
}