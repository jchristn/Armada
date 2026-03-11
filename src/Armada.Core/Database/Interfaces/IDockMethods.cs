namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for docks.
    /// </summary>
    public interface IDockMethods
    {
        /// <summary>
        /// Create a dock.
        /// </summary>
        Task<Dock> CreateAsync(Dock dock, CancellationToken token = default);

        /// <summary>
        /// Read a dock by identifier.
        /// </summary>
        Task<Dock?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Update a dock.
        /// </summary>
        Task<Dock> UpdateAsync(Dock dock, CancellationToken token = default);

        /// <summary>
        /// Delete a dock by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all docks.
        /// </summary>
        Task<List<Dock>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate docks with pagination and filtering.
        /// </summary>
        Task<EnumerationResult<Dock>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate docks by vessel identifier.
        /// </summary>
        Task<List<Dock>> EnumerateByVesselAsync(string vesselId, CancellationToken token = default);

        /// <summary>
        /// Find an available dock for a vessel (no captain assigned, active).
        /// </summary>
        Task<Dock?> FindAvailableAsync(string vesselId, CancellationToken token = default);

        /// <summary>
        /// Check if a dock exists by identifier.
        /// </summary>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);
    }
}
