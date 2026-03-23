namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for users.
    /// </summary>
    public interface IUserMethods
    {
        /// <summary>
        /// Create a user.
        /// </summary>
        Task<UserMaster> CreateAsync(UserMaster user, CancellationToken token = default);

        /// <summary>
        /// Read a user by tenant and identifier (tenant-scoped).
        /// </summary>
        Task<UserMaster?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a user by identifier only (admin, no tenant filter).
        /// </summary>
        Task<UserMaster?> ReadByIdAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a user by email within a tenant (tenant-scoped).
        /// </summary>
        Task<UserMaster?> ReadByEmailAsync(string tenantId, string email, CancellationToken token = default);

        /// <summary>
        /// Read all users matching an email across all tenants (for login flow).
        /// </summary>
        Task<List<UserMaster>> ReadByEmailAnyTenantAsync(string email, CancellationToken token = default);

        /// <summary>
        /// Update a user.
        /// </summary>
        Task<UserMaster> UpdateAsync(UserMaster user, CancellationToken token = default);

        /// <summary>
        /// Delete a user by tenant and identifier.
        /// </summary>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all users in a tenant (tenant-scoped).
        /// </summary>
        Task<List<UserMaster>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Enumerate users with pagination and filtering (tenant-scoped).
        /// </summary>
        Task<EnumerationResult<UserMaster>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate users with pagination and filtering across all tenants (admin, unscoped).
        /// </summary>
        Task<EnumerationResult<UserMaster>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Check if a user exists in a tenant.
        /// </summary>
        Task<bool> ExistsAsync(string tenantId, string id, CancellationToken token = default);
    }
}
