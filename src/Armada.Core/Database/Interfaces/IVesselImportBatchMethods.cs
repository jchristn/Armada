namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for vessel import batches. Deleting a batch also deletes its items.
    /// </summary>
    public interface IVesselImportBatchMethods
    {
        /// <summary>
        /// Create a batch. An empty identifier is backfilled with a new vib_ identifier.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when batch is null.</exception>
        Task<VesselImportBatch> CreateAsync(VesselImportBatch batch, CancellationToken token = default);

        /// <summary>
        /// Read a batch by identifier, or null when not found.
        /// </summary>
        Task<VesselImportBatch?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a batch by tenant and identifier, or null when not found in that tenant.
        /// </summary>
        Task<VesselImportBatch?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Update a batch. LastUpdateUtc is set to now.
        /// </summary>
        Task<VesselImportBatch> UpdateAsync(VesselImportBatch batch, CancellationToken token = default);

        /// <summary>
        /// Delete a batch and its items by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Delete a batch and its items by tenant and identifier.
        /// </summary>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate batches in a tenant with paging (newest first by default). Honors PageNumber, PageSize, Order,
        /// CreatedAfter, CreatedBefore, and Status (a <see cref="Armada.Core.Enums.VesselImportBatchStatusEnum"/> name).
        /// </summary>
        Task<EnumerationResult<VesselImportBatch>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default);
    }
}
