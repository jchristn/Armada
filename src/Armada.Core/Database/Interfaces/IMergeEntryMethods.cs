namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for merge queue entries.
    /// </summary>
    public interface IMergeEntryMethods
    {
        /// <summary>
        /// Create a merge entry.
        /// </summary>
        Task<MergeEntry> CreateAsync(MergeEntry entry, CancellationToken token = default);

        /// <summary>
        /// Read a merge entry by identifier.
        /// </summary>
        Task<MergeEntry?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Update a merge entry.
        /// </summary>
        Task<MergeEntry> UpdateAsync(MergeEntry entry, CancellationToken token = default);

        /// <summary>
        /// Delete a merge entry by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all merge entries.
        /// </summary>
        Task<List<MergeEntry>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate merge entries with pagination and filtering.
        /// </summary>
        Task<EnumerationResult<MergeEntry>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate merge entries by status.
        /// </summary>
        Task<List<MergeEntry>> EnumerateByStatusAsync(MergeStatusEnum status, CancellationToken token = default);

        /// <summary>
        /// Check if a merge entry exists by identifier.
        /// </summary>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a merge entry by tenant and identifier (tenant-scoped).
        /// </summary>
        Task<MergeEntry?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Delete a merge entry by tenant and identifier (tenant-scoped).
        /// </summary>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all merge entries in a tenant (tenant-scoped).
        /// </summary>
        Task<List<MergeEntry>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Enumerate merge entries with pagination and filtering (tenant-scoped).
        /// </summary>
        Task<EnumerationResult<MergeEntry>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate merge entries by tenant and status (tenant-scoped).
        /// </summary>
        Task<List<MergeEntry>> EnumerateByStatusAsync(string tenantId, MergeStatusEnum status, CancellationToken token = default);

        /// <summary>
        /// Check if a merge entry exists by tenant and identifier (tenant-scoped).
        /// </summary>
        Task<bool> ExistsAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a merge entry by tenant, user, and identifier (user-scoped).
        /// </summary>
        Task<MergeEntry?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Delete a merge entry by tenant, user, and identifier (user-scoped).
        /// </summary>
        Task DeleteAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all merge entries owned by a user within a tenant (user-scoped).
        /// </summary>
        Task<List<MergeEntry>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>
        /// Enumerate merge entries with pagination and filtering (user-scoped).
        /// </summary>
        Task<EnumerationResult<MergeEntry>> EnumerateAsync(string tenantId, string userId, EnumerationQuery query, CancellationToken token = default);
    }
}
