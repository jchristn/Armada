namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for vessels.
    /// </summary>
    public interface IVesselMethods
    {
        /// <summary>
        /// Create a vessel.
        /// </summary>
        Task<Vessel> CreateAsync(Vessel vessel, CancellationToken token = default);

        /// <summary>
        /// Read a vessel by identifier.
        /// </summary>
        Task<Vessel?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a vessel by name.
        /// </summary>
        Task<Vessel?> ReadByNameAsync(string name, CancellationToken token = default);

        /// <summary>
        /// Update a vessel.
        /// </summary>
        Task<Vessel> UpdateAsync(Vessel vessel, CancellationToken token = default);

        /// <summary>
        /// Delete a vessel by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all vessels.
        /// </summary>
        Task<List<Vessel>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate vessels with pagination and filtering.
        /// </summary>
        Task<EnumerationResult<Vessel>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate vessels by fleet identifier.
        /// </summary>
        Task<List<Vessel>> EnumerateByFleetAsync(string fleetId, CancellationToken token = default);

        /// <summary>
        /// Check if a vessel exists by identifier.
        /// </summary>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);
    }
}
