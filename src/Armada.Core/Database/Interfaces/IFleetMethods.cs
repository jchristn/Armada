namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for fleets.
    /// </summary>
    public interface IFleetMethods
    {
        /// <summary>
        /// Create a fleet.
        /// </summary>
        Task<Fleet> CreateAsync(Fleet fleet, CancellationToken token = default);

        /// <summary>
        /// Read a fleet by identifier.
        /// </summary>
        Task<Fleet?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a fleet by name.
        /// </summary>
        Task<Fleet?> ReadByNameAsync(string name, CancellationToken token = default);

        /// <summary>
        /// Update a fleet.
        /// </summary>
        Task<Fleet> UpdateAsync(Fleet fleet, CancellationToken token = default);

        /// <summary>
        /// Delete a fleet by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all fleets.
        /// </summary>
        Task<List<Fleet>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate fleets with pagination and filtering.
        /// </summary>
        Task<EnumerationResult<Fleet>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Check if a fleet exists by identifier.
        /// </summary>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a fleet by tenant and identifier (tenant-scoped).
        /// </summary>
        Task<Fleet?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Delete a fleet by tenant and identifier (tenant-scoped).
        /// </summary>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all fleets in a tenant (tenant-scoped).
        /// </summary>
        Task<List<Fleet>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Enumerate fleets with pagination and filtering (tenant-scoped).
        /// </summary>
        Task<EnumerationResult<Fleet>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Read a fleet by tenant and name (tenant-scoped).
        /// </summary>
        Task<Fleet?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default);

        /// <summary>
        /// Check if a fleet exists by tenant and identifier (tenant-scoped).
        /// </summary>
        Task<bool> ExistsAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a fleet by tenant, user, and identifier (user-scoped).
        /// </summary>
        Task<Fleet?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Delete a fleet by tenant, user, and identifier (user-scoped).
        /// </summary>
        Task DeleteAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all fleets owned by a user within a tenant (user-scoped).
        /// </summary>
        Task<List<Fleet>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>
        /// Enumerate fleets with pagination and filtering (user-scoped).
        /// </summary>
        Task<EnumerationResult<Fleet>> EnumerateAsync(string tenantId, string userId, EnumerationQuery query, CancellationToken token = default);
    }
}
