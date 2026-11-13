namespace Armada.Core.Services.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Service for voyage lifecycle management.
    /// </summary>
    public interface IVoyageService
    {
        /// <summary>
        /// Check all active voyages and mark complete if all child missions are done.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of voyages that were marked complete during this check.</returns>
        Task<List<Voyage>> CheckCompletionsAsync(CancellationToken token = default);

        /// <summary>
        /// Check all active voyages and mark complete if all child missions are done, treating a WorkProduced mission whose
        /// completion is still being processed as active.
        /// </summary>
        /// <param name="isCompletionInFlight">Returns true for a mission whose completion handler is still running.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of voyages that were marked complete during this check.</returns>
        Task<List<Voyage>> CheckCompletionsAsync(Func<string, bool>? isCompletionInFlight, CancellationToken token = default);

        /// <summary>
        /// Get progress details for a specific voyage.
        /// </summary>
        /// <param name="voyageId">Voyage identifier.</param>
        /// <param name="tenantId">Optional tenant ID for tenant-scoped reads. Null for system/admin context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Voyage progress or null if not found.</returns>
        Task<VoyageProgress?> GetProgressAsync(string voyageId, string? tenantId = null, CancellationToken token = default);

        /// <summary>
        /// Get progress for a voyage already read under the caller's scope (counts only; no mission objects).
        /// </summary>
        /// <param name="voyage">Voyage.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Voyage progress.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="voyage"/> is null.</exception>
        Task<VoyageProgress> GetProgressAsync(Voyage voyage, CancellationToken token = default);

        /// <summary>
        /// Enumerate voyage progress one page at a time, paged in the database. With no Status filter the view is the
        /// active voyages (InProgress first, then Open, newest first within each); a Status filter selects one status.
        /// The caller's tenant and user scope applies before counting.
        /// </summary>
        /// <param name="auth">Caller.</param>
        /// <param name="query">Paging and filters (Status, CreatedAfter, CreatedBefore, Order).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of progress.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ArgumentException">Thrown when Status is not a voyage status.</exception>
        Task<EnumerationResult<VoyageProgress>> EnumerateProgressAsync(AuthContext auth, EnumerationQuery query, CancellationToken token = default);
    }
}
