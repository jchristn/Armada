namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for fleet action run targets. Targets are history: they are not deleted when the
    /// target vessel is deleted.
    /// </summary>
    public interface IFleetActionRunTargetMethods
    {
        /// <summary>
        /// Create a target. An empty identifier is backfilled with a new fat_ identifier.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when target is null.</exception>
        Task<FleetActionRunTarget> CreateAsync(FleetActionRunTarget target, CancellationToken token = default);

        /// <summary>
        /// Read a target by identifier, or null when not found.
        /// </summary>
        Task<FleetActionRunTarget?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a target by tenant and identifier, or null when not found in that tenant.
        /// </summary>
        Task<FleetActionRunTarget?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Update a target. LastUpdateUtc is set to now.
        /// </summary>
        Task<FleetActionRunTarget> UpdateAsync(FleetActionRunTarget target, CancellationToken token = default);

        /// <summary>
        /// Enumerate the targets of a run within a tenant with paging, optionally filtered by status, ordered by
        /// vessel name then identifier. pageNumber is clamped to at least 1 and pageSize to 1 through 1000.
        /// </summary>
        Task<EnumerationResult<FleetActionRunTarget>> EnumerateByRunAsync(string tenantId, string runId, FleetActionTargetStatusEnum? status, int pageNumber, int pageSize, CancellationToken token = default);

        /// <summary>
        /// Read every target of a run (any tenant), ordered by vessel name then identifier. Used by the runner.
        /// </summary>
        Task<List<FleetActionRunTarget>> ReadAllByRunAsync(string runId, CancellationToken token = default);

        /// <summary>
        /// Delete every target of a run.
        /// </summary>
        Task DeleteByRunAsync(string runId, CancellationToken token = default);
    }
}
