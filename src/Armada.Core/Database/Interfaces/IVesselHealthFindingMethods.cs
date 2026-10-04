namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for vessel health findings (one row per criterion per vessel). Deleting a vessel
    /// deletes its findings.
    /// </summary>
    public interface IVesselHealthFindingMethods
    {
        /// <summary>
        /// Atomically replace every finding for a vessel within a tenant with the supplied list. Each finding's
        /// TenantId and VesselId are overwritten with the supplied values, and empty identifiers are backfilled.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when tenantId, vesselId, or findings is null or empty.</exception>
        Task ReplaceForVesselAsync(string tenantId, string vesselId, List<VesselHealthFinding> findings, CancellationToken token = default);

        /// <summary>
        /// Read every finding for a vessel within a tenant, ordered by criterion.
        /// </summary>
        Task<List<VesselHealthFinding>> ReadByVesselAsync(string tenantId, string vesselId, CancellationToken token = default);

        /// <summary>
        /// Delete every finding for a vessel within a tenant.
        /// </summary>
        Task DeleteByVesselAsync(string tenantId, string vesselId, CancellationToken token = default);
    }
}
