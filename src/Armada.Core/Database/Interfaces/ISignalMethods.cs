namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for signals.
    /// </summary>
    public interface ISignalMethods
    {
        /// <summary>
        /// Create a signal.
        /// </summary>
        Task<Signal> CreateAsync(Signal signal, CancellationToken token = default);

        /// <summary>
        /// Read a signal by identifier.
        /// </summary>
        Task<Signal?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate signals by recipient captain identifier.
        /// </summary>
        Task<List<Signal>> EnumerateByRecipientAsync(string captainId, bool unreadOnly = true, CancellationToken token = default);

        /// <summary>
        /// Enumerate recent signals.
        /// </summary>
        Task<List<Signal>> EnumerateRecentAsync(int count = 50, CancellationToken token = default);

        /// <summary>
        /// Mark a signal as read.
        /// </summary>
        Task MarkReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Mark a signal as read (tenant-scoped).
        /// </summary>
        Task MarkReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Permanently delete a signal by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate signals with pagination and filtering.
        /// </summary>
        Task<EnumerationResult<Signal>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Read a signal by tenant and identifier (tenant-scoped).
        /// </summary>
        Task<Signal?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Delete a signal by tenant and identifier (tenant-scoped).
        /// </summary>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all signals for a tenant.
        /// </summary>
        Task<List<Signal>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Enumerate signals with pagination and filtering (tenant-scoped).
        /// </summary>
        Task<EnumerationResult<Signal>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate signals by tenant and recipient captain identifier (tenant-scoped).
        /// </summary>
        Task<List<Signal>> EnumerateByRecipientAsync(string tenantId, string captainId, bool unreadOnly = true, CancellationToken token = default);

        /// <summary>
        /// Enumerate recent signals (tenant-scoped).
        /// </summary>
        Task<List<Signal>> EnumerateRecentAsync(string tenantId, int count = 50, CancellationToken token = default);

        /// <summary>
        /// Read a signal by tenant, user, and identifier (user-scoped).
        /// </summary>
        Task<Signal?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Delete a signal by tenant, user, and identifier (user-scoped).
        /// </summary>
        Task DeleteAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all signals owned by a user within a tenant (user-scoped).
        /// </summary>
        Task<List<Signal>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>
        /// Enumerate signals with pagination and filtering (user-scoped).
        /// </summary>
        Task<EnumerationResult<Signal>> EnumerateAsync(string tenantId, string userId, EnumerationQuery query, CancellationToken token = default);
    }
}
