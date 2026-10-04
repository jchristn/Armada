namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for a vessel's outdated or vulnerable dependencies. Deleting a vessel deletes its
    /// dependency rows.
    /// </summary>
    public interface IVesselDependencyMethods
    {
        /// <summary>
        /// Atomically replace every dependency row for a vessel within a tenant with the supplied list. Each row's
        /// TenantId and VesselId are overwritten with the supplied values, and empty identifiers are backfilled.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when tenantId, vesselId, or dependencies is null or empty.</exception>
        Task ReplaceForVesselAsync(string tenantId, string vesselId, List<VesselDependency> dependencies, CancellationToken token = default);

        /// <summary>
        /// Read every dependency row for a vessel within a tenant, ordered by ecosystem, package name, then project
        /// path.
        /// </summary>
        Task<List<VesselDependency>> ReadByVesselAsync(string tenantId, string vesselId, CancellationToken token = default);

        /// <summary>
        /// Delete every dependency row for a vessel within a tenant.
        /// </summary>
        Task DeleteByVesselAsync(string tenantId, string vesselId, CancellationToken token = default);
    }
}
