namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for registered Harbors (host runners) and their advertised capabilities.
    /// </summary>
    public interface IHarborMethods
    {
        /// <summary>
        /// Creates a new Harbor, including its capability rows.
        /// </summary>
        Task<Harbor> CreateAsync(Harbor harbor, CancellationToken token = default);

        /// <summary>
        /// Updates an existing Harbor and replaces its capability rows.
        /// </summary>
        Task<Harbor> UpdateAsync(Harbor harbor, CancellationToken token = default);

        /// <summary>
        /// Reads a Harbor by its identifier, or null when not found.
        /// </summary>
        Task<Harbor?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Reads a Harbor for a specific tenant, or null when not found.
        /// </summary>
        Task<Harbor?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Reads a Harbor for a specific tenant and user, or null when not found.
        /// </summary>
        Task<Harbor?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Deletes a Harbor (and its capability rows) by its identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Deletes a Harbor (and its capability rows) for a specific tenant.
        /// </summary>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerates all Harbors (newest first).
        /// </summary>
        Task<List<Harbor>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerates Harbors for a specific tenant (newest first).
        /// </summary>
        Task<List<Harbor>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Enumerates Harbors for a specific tenant and user (newest first).
        /// </summary>
        Task<List<Harbor>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>
        /// Whether any Harbor exists.
        /// </summary>
        Task<bool> ExistsAnyAsync(CancellationToken token = default);

        /// <summary>
        /// Whether a Harbor with the given id exists.
        /// </summary>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);
    }
}
