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
        /// Enumerate one page of the active, non-system tenants in which a user with this email exists, ordered by
        /// tenant name then id, paged and counted in the database (for the login tenant picker).
        /// </summary>
        /// <param name="email">Email address, already normalized to lower case.</param>
        /// <param name="query">Paging (PageNumber, PageSize).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of tenants.</returns>
        Task<EnumerationResult<TenantListEntry>> EnumerateTenantsByEmailAsync(string email, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Update a user.
        /// </summary>
        Task<UserMaster> UpdateAsync(UserMaster user, CancellationToken token = default);

        /// <summary>
        /// Replace <c>password_sha256</c> when the stored hash is still <paramref name="expectedHash"/>.
        /// No other column is written. A row whose hash has changed is left as stored.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">User identifier.</param>
        /// <param name="expectedHash">Hash the row must still hold.</param>
        /// <param name="replacementHash">Adaptive hash to store.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when exactly one row was updated.</returns>
        Task<bool> UpgradePasswordAsync(string tenantId, string id, string expectedHash, string replacementHash, CancellationToken token = default);

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
