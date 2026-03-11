namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for voyages.
    /// </summary>
    public interface IVoyageMethods
    {
        /// <summary>
        /// Create a voyage.
        /// </summary>
        Task<Voyage> CreateAsync(Voyage voyage, CancellationToken token = default);

        /// <summary>
        /// Read a voyage by identifier.
        /// </summary>
        Task<Voyage?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Update a voyage.
        /// </summary>
        Task<Voyage> UpdateAsync(Voyage voyage, CancellationToken token = default);

        /// <summary>
        /// Delete a voyage by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all voyages.
        /// </summary>
        Task<List<Voyage>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate voyages with pagination and filtering.
        /// </summary>
        Task<EnumerationResult<Voyage>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate voyages by status.
        /// </summary>
        Task<List<Voyage>> EnumerateByStatusAsync(VoyageStatusEnum status, CancellationToken token = default);

        /// <summary>
        /// Check if a voyage exists by identifier.
        /// </summary>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);
    }
}
