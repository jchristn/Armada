namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for tenants.
    /// </summary>
    public interface ITenantMethods
    {
        /// <summary>
        /// Create a tenant.
        /// </summary>
        Task<TenantMetadata> CreateAsync(TenantMetadata tenant, CancellationToken token = default);

        /// <summary>
        /// Read a tenant by identifier.
        /// </summary>
        Task<TenantMetadata?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a tenant by name.
        /// </summary>
        Task<TenantMetadata?> ReadByNameAsync(string name, CancellationToken token = default);

        /// <summary>
        /// Update a tenant.
        /// </summary>
        Task<TenantMetadata> UpdateAsync(TenantMetadata tenant, CancellationToken token = default);

        /// <summary>
        /// Delete a tenant by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all tenants.
        /// </summary>
        Task<List<TenantMetadata>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate tenants with pagination and filtering.
        /// </summary>
        Task<EnumerationResult<TenantMetadata>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Check if a tenant exists by identifier.
        /// </summary>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Check if any tenant exists in the database.
        /// Used for first-boot detection.
        /// </summary>
        Task<bool> ExistsAnyAsync(CancellationToken token = default);
    }
}
