namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for fleet action runs. Deleting a run also deletes its targets.
    /// </summary>
    public interface IFleetActionRunMethods
    {
        /// <summary>
        /// Create a run. An empty identifier is backfilled with a new far_ identifier.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when run is null.</exception>
        Task<FleetActionRun> CreateAsync(FleetActionRun run, CancellationToken token = default);

        /// <summary>
        /// Read a run by identifier, or null when not found.
        /// </summary>
        Task<FleetActionRun?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a run by tenant and identifier, or null when not found in that tenant.
        /// </summary>
        Task<FleetActionRun?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Update a run. LastUpdateUtc is set to now.
        /// </summary>
        Task<FleetActionRun> UpdateAsync(FleetActionRun run, CancellationToken token = default);

        /// <summary>
        /// Delete a run and its targets by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Delete a run and its targets by tenant and identifier.
        /// </summary>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate runs in a tenant with paging (newest first by default). Honors PageNumber, PageSize, Order,
        /// CreatedAfter, CreatedBefore, and Status (a <see cref="Armada.Core.Enums.FleetActionRunStatusEnum"/> name;
        /// an unrecognized value matches nothing).
        /// </summary>
        Task<EnumerationResult<FleetActionRun>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate every run, across all tenants, whose status is Pending or Running (oldest first). Used by the
        /// runner on startup and during synchronization.
        /// </summary>
        Task<List<FleetActionRun>> EnumerateUnfinishedAsync(CancellationToken token = default);
    }
}
