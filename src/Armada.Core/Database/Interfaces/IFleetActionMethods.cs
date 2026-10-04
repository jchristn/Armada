namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for fleet action definitions. Deleting a built-in action (IsBuiltIn = true) is a soft
    /// delete: the row is kept with Active = false so first-boot seeding (which checks
    /// <see cref="ReadByBuiltInKeyAsync"/>, including inactive rows) does not re-create it. Deleting a
    /// user-defined action removes the row.
    /// </summary>
    public interface IFleetActionMethods
    {
        /// <summary>
        /// Create an action. An empty identifier is backfilled with a new fac_ identifier.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when action is null.</exception>
        Task<FleetAction> CreateAsync(FleetAction action, CancellationToken token = default);

        /// <summary>
        /// Read an action by identifier (active or inactive), or null when not found.
        /// </summary>
        Task<FleetAction?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read an action by tenant and identifier (active or inactive), or null when not found in that tenant.
        /// </summary>
        Task<FleetAction?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a built-in action by its stable key within a tenant, including soft-deleted (inactive) rows, or
        /// null when the built-in has never been seeded in that tenant.
        /// </summary>
        Task<FleetAction?> ReadByBuiltInKeyAsync(string tenantId, string builtInKey, CancellationToken token = default);

        /// <summary>
        /// Update an action. LastUpdateUtc is set to now.
        /// </summary>
        Task<FleetAction> UpdateAsync(FleetAction action, CancellationToken token = default);

        /// <summary>
        /// Delete an action by identifier: soft delete (Active = false) for a built-in, hard delete otherwise.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Delete an action by tenant and identifier: soft delete (Active = false) for a built-in, hard delete
        /// otherwise.
        /// </summary>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate actions in a tenant with paging (newest first by default). Honors PageNumber, PageSize,
        /// Order, CreatedAfter, and CreatedBefore. Inactive actions are excluded unless includeInactive is true.
        /// </summary>
        Task<EnumerationResult<FleetAction>> EnumerateAsync(string tenantId, EnumerationQuery query, bool includeInactive = false, CancellationToken token = default);
    }
}
