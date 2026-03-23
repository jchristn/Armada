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
        /// Get progress details for a specific voyage.
        /// </summary>
        /// <param name="voyageId">Voyage identifier.</param>
        /// <param name="tenantId">Optional tenant ID for tenant-scoped reads. Null for system/admin context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Voyage progress or null if not found.</returns>
        Task<VoyageProgress?> GetProgressAsync(string voyageId, string? tenantId = null, CancellationToken token = default);
    }
}
