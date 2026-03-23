namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for credentials.
    /// </summary>
    public interface ICredentialMethods
    {
        /// <summary>
        /// Create a credential.
        /// </summary>
        Task<Credential> CreateAsync(Credential credential, CancellationToken token = default);

        /// <summary>
        /// Read a credential by tenant and identifier (tenant-scoped).
        /// </summary>
        Task<Credential?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a credential by identifier only (admin, no tenant filter).
        /// </summary>
        Task<Credential?> ReadByIdAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a credential by bearer token (global lookup for auth).
        /// </summary>
        Task<Credential?> ReadByBearerTokenAsync(string bearerToken, CancellationToken token = default);

        /// <summary>
        /// Update a credential.
        /// </summary>
        Task<Credential> UpdateAsync(Credential credential, CancellationToken token = default);

        /// <summary>
        /// Delete a credential by tenant and identifier.
        /// </summary>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all credentials in a tenant.
        /// </summary>
        Task<List<Credential>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Enumerate credentials with pagination and filtering (tenant-scoped).
        /// </summary>
        Task<EnumerationResult<Credential>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate credentials with pagination and filtering across all tenants (admin, unscoped).
        /// </summary>
        Task<EnumerationResult<Credential>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate credentials for a specific user in a tenant.
        /// </summary>
        Task<List<Credential>> EnumerateByUserAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>
        /// Enumerate credentials for a specific user in a tenant with pagination and filtering.
        /// </summary>
        Task<EnumerationResult<Credential>> EnumerateByUserAsync(string tenantId, string userId, EnumerationQuery query, CancellationToken token = default);
    }
}
