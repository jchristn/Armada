namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for durable agent memories and their tags. Enumeration returns full lists;
    /// scope filtering, search, and paging are applied above this layer in the service.
    /// </summary>
    public interface IMemoryMethods
    {
        /// <summary>
        /// Creates a new memory, including its tag rows.
        /// </summary>
        Task<Memory> CreateAsync(Memory memory, CancellationToken token = default);

        /// <summary>
        /// Updates an existing memory and replaces its tag rows.
        /// </summary>
        Task<Memory> UpdateAsync(Memory memory, CancellationToken token = default);

        /// <summary>
        /// Reads a memory by its identifier, or null when not found.
        /// </summary>
        Task<Memory?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Reads a memory for a specific tenant, or null when not found.
        /// </summary>
        Task<Memory?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Reads a memory for a specific tenant and user, or null when not found.
        /// </summary>
        Task<Memory?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Reads a memory by its stable idempotency key within a tenant, or null when not found. Used to
        /// upsert a memory in place instead of creating a duplicate.
        /// </summary>
        Task<Memory?> ReadByKeyAsync(string tenantId, string key, CancellationToken token = default);

        /// <summary>
        /// Deletes a memory (and its tag rows) by its identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Deletes a memory (and its tag rows) for a specific tenant.
        /// </summary>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerates all memories (newest first).
        /// </summary>
        Task<List<Memory>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerates memories for a specific tenant (newest first).
        /// </summary>
        Task<List<Memory>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Enumerates memories for a specific tenant and user (newest first).
        /// </summary>
        Task<List<Memory>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>
        /// Whether any memory exists.
        /// </summary>
        Task<bool> ExistsAnyAsync(CancellationToken token = default);

        /// <summary>
        /// Whether a memory with the given id exists.
        /// </summary>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);
    }
}
