namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for fleets.
    /// </summary>
    public interface IFleetMethods
    {
        /// <summary>
        /// Create a fleet.
        /// </summary>
        Task<Fleet> CreateAsync(Fleet fleet, CancellationToken token = default);

        /// <summary>
        /// Read a fleet by identifier.
        /// </summary>
        Task<Fleet?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a fleet by name.
        /// </summary>
        Task<Fleet?> ReadByNameAsync(string name, CancellationToken token = default);

        /// <summary>
        /// Update a fleet.
        /// </summary>
        Task<Fleet> UpdateAsync(Fleet fleet, CancellationToken token = default);

        /// <summary>
        /// Delete a fleet by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all fleets.
        /// </summary>
        Task<List<Fleet>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate fleets with pagination and filtering.
        /// </summary>
        Task<EnumerationResult<Fleet>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Check if a fleet exists by identifier.
        /// </summary>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);
    }
}
