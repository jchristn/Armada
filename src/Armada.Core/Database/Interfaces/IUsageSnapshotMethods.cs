namespace Armada.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for usage snapshots (usage_snapshots), their captain member links
    /// (usage_snapshot_captains) and the per-account latest state (usage_latest_state). A snapshot and
    /// its member links are written in one transaction, so a group attempt is stored exactly once with the
    /// members it covered. Snapshots are derived records: there is no update or destructive CRUD; retention
    /// owns deletion. Every read is tenant-scoped.
    /// </summary>
    public interface IUsageSnapshotMethods
    {
        /// <summary>
        /// Store one snapshot and its member-captain links in a single transaction. The snapshot's tenant,
        /// host and observation time are copied onto each link. Duplicate member ids are stored once.
        /// </summary>
        /// <param name="snapshot">Sanitized snapshot to store.</param>
        /// <param name="captainIds">Captains that were members of the captured account group, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored snapshot.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the snapshot, its tenant, host or any member id is null or empty.</exception>
        Task<UsageSnapshot> CreateAsync(UsageSnapshot snapshot, List<string>? captainIds, CancellationToken token = default);

        /// <summary>
        /// Read one snapshot by id.
        /// </summary>
        /// <param name="id">Snapshot identifier.</param>
        /// <param name="tenantId">Required tenant scope; a snapshot of another tenant is not returned.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The snapshot, or null.</returns>
        Task<UsageSnapshot?> ReadAsync(string id, string tenantId, CancellationToken token = default);

        /// <summary>
        /// One page of snapshots, newest observation first with a stable id tie-break. The captain filter
        /// matches the triggering captain or any member link.
        /// </summary>
        /// <param name="query">Filters and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        Task<EnumerationResult<UsageSnapshot>> EnumerateAsync(UsageSnapshotQuery query, CancellationToken token = default);

        /// <summary>
        /// Captains that were members of a snapshot's account group at capture time (the snapshot's links).
        /// </summary>
        /// <param name="snapshotId">Snapshot identifier.</param>
        /// <param name="tenantId">Required tenant scope.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The member captain ids, ordered stably.</returns>
        Task<List<string>> ReadCaptainsAsync(string snapshotId, string tenantId, CancellationToken token = default);

        /// <summary>
        /// Atomically accumulate hourly rollups and delete at most 500 snapshots before the cutoff with their member links. Return the
        /// number of snapshots removed. Used by the retention pass; deletes both tables consistently.
        /// </summary>
        /// <param name="capturedBeforeUtc">Exclusive cutoff on the observation time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of snapshots deleted.</returns>
        Task<int> DeleteOlderThanAsync(DateTime capturedBeforeUtc, CancellationToken token = default);

        /// <summary>
        /// Upsert the per-account latest state. The row is keyed by tenant, host, runtime and account key.
        /// The caller decides whether the write updates the last-good summary or the separate latest-error
        /// fields; storage never overwrites the last good state with an error and never refreshes an
        /// expired row. Each summary is replaced as one unit: when LastGoodSnapshotId is set, every
        /// last-good field (including null windows and timestamps) is taken from the state, so the summary
        /// always describes exactly the selected snapshot; when it is null or empty, every stored last-good
        /// field is preserved unchanged. The latest-error fields follow the same rule keyed on
        /// LastErrorSnapshotId.
        /// </summary>
        /// <param name="state">State to store.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored state.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the state, its tenant, host or account key is null or empty.</exception>
        Task<UsageLatestState> UpsertLatestStateAsync(UsageLatestState state, CancellationToken token = default);

        /// <summary>
        /// Read the per-account latest state.
        /// </summary>
        /// <param name="tenantId">Required tenant scope.</param>
        /// <param name="hostId">Credential host id.</param>
        /// <param name="runtime">Runtime.</param>
        /// <param name="accountKey">Opaque account key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The state, or null when the account has none.</returns>
        Task<UsageLatestState?> ReadLatestStateAsync(string tenantId, string hostId, AgentRuntimeEnum runtime, string accountKey, CancellationToken token = default);

        /// <summary>
        /// One page of per-account latest-state rows, ordered stably by account key then id.
        /// </summary>
        /// <param name="query">Filters and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        Task<EnumerationResult<UsageLatestState>> EnumerateLatestStatesAsync(UsageLatestStateQuery query, CancellationToken token = default);

        /// <summary>Remove expired latest-state rows and closed memberships outside the retained history window. Active account metadata is preserved.</summary>
        /// <param name="closedBeforeUtc">Exclusive close/expiry cutoff.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Metadata rows removed.</returns>
        Task<int> PruneInactiveStateAsync(DateTime closedBeforeUtc, CancellationToken token = default);

        /// <summary>
        /// Delete every usage snapshot, member link and latest-state row of a tenant (tenant deletion
        /// cascade). Returns the number of snapshots removed.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of snapshots deleted.</returns>
        Task<int> DeleteByTenantAsync(string tenantId, CancellationToken token = default);
    }
}