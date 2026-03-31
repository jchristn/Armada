namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for docks.
    /// </summary>
    public interface IDockMethods
    {
        /// <summary>
        /// Create a dock.
        /// </summary>
        Task<Dock> CreateAsync(Dock dock, CancellationToken token = default);

        /// <summary>
        /// Read a dock by identifier.
        /// </summary>
        Task<Dock?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Update a dock.
        /// </summary>
        Task<Dock> UpdateAsync(Dock dock, CancellationToken token = default);

        /// <summary>
        /// Delete a dock by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all docks.
        /// </summary>
        Task<List<Dock>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate docks with pagination and filtering.
        /// </summary>
        Task<EnumerationResult<Dock>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate docks by vessel identifier.
        /// </summary>
        Task<List<Dock>> EnumerateByVesselAsync(string vesselId, CancellationToken token = default);

        /// <summary>
        /// Find an available dock for a vessel (no captain assigned, active).
        /// </summary>
        Task<Dock?> FindAvailableAsync(string vesselId, CancellationToken token = default);

        /// <summary>
        /// Check if a dock exists by identifier.
        /// </summary>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a dock by tenant and identifier (tenant-scoped).
        /// </summary>
        Task<Dock?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Delete a dock by tenant and identifier (tenant-scoped).
        /// </summary>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all docks in a tenant (tenant-scoped).
        /// </summary>
        Task<List<Dock>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Enumerate docks with pagination and filtering (tenant-scoped).
        /// </summary>
        Task<EnumerationResult<Dock>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate docks by tenant and vessel identifier (tenant-scoped).
        /// </summary>
        Task<List<Dock>> EnumerateByVesselAsync(string tenantId, string vesselId, CancellationToken token = default);

        /// <summary>
        /// Find an available dock for a vessel within a tenant (tenant-scoped).
        /// </summary>
        Task<Dock?> FindAvailableAsync(string tenantId, string vesselId, CancellationToken token = default);

        /// <summary>
        /// Check if a dock exists by tenant and identifier (tenant-scoped).
        /// </summary>
        Task<bool> ExistsAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a dock by tenant, user, and identifier (user-scoped).
        /// </summary>
        Task<Dock?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Delete a dock by tenant, user, and identifier (user-scoped).
        /// </summary>
        Task DeleteAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all docks owned by a user within a tenant (user-scoped).
        /// </summary>
        Task<List<Dock>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>
        /// Enumerate docks with pagination and filtering (user-scoped).
        /// </summary>
        Task<EnumerationResult<Dock>> EnumerateAsync(string tenantId, string userId, EnumerationQuery query, CancellationToken token = default);
    }
}
