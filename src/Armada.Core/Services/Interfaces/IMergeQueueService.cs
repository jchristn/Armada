namespace Armada.Core.Services.Interfaces
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Service for managing the merge queue — sequential test-before-merge with immediate landing.
    /// </summary>
    public interface IMergeQueueService
    {
        /// <summary>
        /// Enqueue a branch for testing and merging.
        /// </summary>
        /// <param name="entry">Merge entry to enqueue.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created merge entry.</returns>
        Task<MergeEntry> EnqueueAsync(MergeEntry entry, CancellationToken token = default);

        /// <summary>
        /// Process queued entries one at a time: merge, test, land immediately, then move to the next.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        Task ProcessQueueAsync(CancellationToken token = default);

        /// <summary>
        /// Cancel a queued entry.
        /// </summary>
        /// <param name="entryId">Merge entry identifier.</param>
        /// <param name="tenantId">Optional tenant ID for tenant-scoped reads. Null for system/admin context.</param>
        /// <param name="token">Cancellation token.</param>
        Task CancelAsync(string entryId, string? tenantId = null, CancellationToken token = default);

        /// <summary>
        /// List all entries in the queue.
        /// </summary>
        /// <param name="tenantId">Optional tenant ID for tenant-scoped enumeration. Null for system/admin context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of merge entries.</returns>
        Task<List<MergeEntry>> ListAsync(string? tenantId = null, CancellationToken token = default);

        /// <summary>
        /// Process a single merge queue entry by ID.
        /// </summary>
        /// <param name="entryId">Merge entry identifier.</param>
        /// <param name="tenantId">Optional tenant ID for tenant-scoped reads. Null for system/admin context.</param>
        /// <param name="token">Cancellation token.</param>
        Task<MergeEntry?> ProcessSingleAsync(string entryId, string? tenantId = null, CancellationToken token = default);

        /// <summary>
        /// Get a specific merge entry.
        /// </summary>
        /// <param name="entryId">Merge entry identifier.</param>
        /// <param name="tenantId">Optional tenant ID for tenant-scoped reads. Null for system/admin context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The merge entry or null.</returns>
        Task<MergeEntry?> GetAsync(string entryId, string? tenantId = null, CancellationToken token = default);

        /// <summary>
        /// Permanently delete a merge entry from the database.
        /// Only entries in terminal states (Cancelled, Landed, Failed) can be deleted.
        /// </summary>
        /// <param name="entryId">Merge entry identifier.</param>
        /// <param name="tenantId">Optional tenant ID for tenant-scoped reads. Null for system/admin context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if deleted, false if not found or not in a terminal state.</returns>
        Task<bool> DeleteAsync(string entryId, string? tenantId = null, CancellationToken token = default);

        /// <summary>
        /// Permanently delete multiple merge queue entries by ID.
        /// Only entries in terminal states (Cancelled, Landed, Failed) can be deleted.
        /// </summary>
        /// <param name="entryIds">List of merge entry identifiers to delete.</param>
        /// <param name="tenantId">Optional tenant ID for tenant-scoped reads. Null for system/admin context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Purge result with counts of purged and skipped entries.</returns>
        Task<MergeQueuePurgeResult> DeleteMultipleAsync(List<string> entryIds, string? tenantId = null, CancellationToken token = default);

        /// <summary>
        /// Permanently delete all terminal merge queue entries (Landed, Failed, Cancelled).
        /// Optionally filter by vessel ID and/or status.
        /// </summary>
        /// <param name="vesselId">Optional vessel ID filter.</param>
        /// <param name="status">Optional status filter (must be a terminal status).</param>
        /// <param name="tenantId">Optional tenant ID for tenant-scoped enumeration. Null for system/admin context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of entries deleted.</returns>
        Task<int> PurgeTerminalAsync(string? vesselId = null, MergeStatusEnum? status = null, string? tenantId = null, CancellationToken token = default);
    }
}
