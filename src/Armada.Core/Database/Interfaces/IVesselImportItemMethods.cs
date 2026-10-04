namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for vessel import items (candidates within a batch). Items are unique per tenant,
    /// batch, and path. Items are history and are not deleted when a vessel is deleted.
    /// </summary>
    public interface IVesselImportItemMethods
    {
        /// <summary>
        /// Create an item. An empty identifier is backfilled with a new vii_ identifier.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when item is null.</exception>
        Task<VesselImportItem> CreateAsync(VesselImportItem item, CancellationToken token = default);

        /// <summary>
        /// Create many items in a single transaction. Empty identifiers are backfilled.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when items is null.</exception>
        Task<List<VesselImportItem>> CreateManyAsync(List<VesselImportItem> items, CancellationToken token = default);

        /// <summary>
        /// Read an item by identifier, or null when not found.
        /// </summary>
        Task<VesselImportItem?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read an item by tenant and identifier, or null when not found in that tenant.
        /// </summary>
        Task<VesselImportItem?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Update an item. LastUpdateUtc is set to now.
        /// </summary>
        Task<VesselImportItem> UpdateAsync(VesselImportItem item, CancellationToken token = default);

        /// <summary>
        /// Enumerate every item in a batch within a tenant, ordered by path.
        /// </summary>
        Task<List<VesselImportItem>> EnumerateByBatchAsync(string tenantId, string batchId, CancellationToken token = default);

        /// <summary>
        /// Delete every item in a batch within a tenant.
        /// </summary>
        Task DeleteByBatchAsync(string tenantId, string batchId, CancellationToken token = default);
    }
}
