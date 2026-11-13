namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for usage hourly rollups (usage_hourly_rollups): the bounded per-account
    /// utilization aggregates the retention pass computes from raw snapshots before deleting them. Upserts
    /// are idempotent on the natural key (tenant, host, runtime, account key, window name, model, hour), so
    /// an interrupted rollup job can be retried without double-counting.
    /// </summary>
    public interface IUsageHourlyRollupMethods
    {
        /// <summary>
        /// Insert or update the rollup for its natural key: tenant, host, runtime, provider, account key,
        /// window name, model and UTC hour. Re-running the same rollup replaces the row with the
        /// recomputed values; nothing is accumulated twice. A null or empty provider is stored as the
        /// empty string and is its own key value (an unknown provider), distinct from every named
        /// provider on all four database providers; it reads back as null.
        /// </summary>
        /// <param name="rollup">Rollup to store.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored rollup.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the rollup or any required key field is null or empty.</exception>
        Task<UsageHourlyRollup> UpsertAsync(UsageHourlyRollup rollup, CancellationToken token = default);

        /// <summary>
        /// Read one rollup by id.
        /// </summary>
        /// <param name="id">Rollup identifier.</param>
        /// <param name="tenantId">Required tenant scope.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The rollup, or null.</returns>
        Task<UsageHourlyRollup?> ReadAsync(string id, string tenantId, CancellationToken token = default);

        /// <summary>
        /// One page of rollups, newest hour first with a stable id tie-break.
        /// </summary>
        /// <param name="query">Filters and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        Task<EnumerationResult<UsageHourlyRollup>> EnumerateAsync(UsageRollupQuery query, CancellationToken token = default);

        /// <summary>
        /// Delete rollups for hours starting before the cutoff and return the number removed.
        /// </summary>
        /// <param name="hourBeforeUtc">Exclusive cutoff on the hour bucket start.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of rollups deleted.</returns>
        Task<int> DeleteOlderThanAsync(DateTime hourBeforeUtc, CancellationToken token = default);

        /// <summary>
        /// Delete every rollup of a tenant (tenant deletion cascade).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of rollups deleted.</returns>
        Task<int> DeleteByTenantAsync(string tenantId, CancellationToken token = default);
    }
}