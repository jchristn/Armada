namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for manual vessel health overrides, unique per tenant, vessel, and criterion.
    /// Deleting a vessel deletes its overrides.
    /// </summary>
    public interface IVesselHealthOverrideMethods
    {
        /// <summary>
        /// Insert or replace the override for (TenantId, VesselId, Criterion). An existing override keeps its
        /// identifier and CreatedUtc; a new one backfills an empty identifier. LastUpdateUtc is set to now.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when healthOverride is null.</exception>
        /// <exception cref="ArgumentException">Thrown when VesselId or TenantId is empty.</exception>
        Task<VesselHealthOverride> UpsertAsync(VesselHealthOverride healthOverride, CancellationToken token = default);

        /// <summary>
        /// Read every override for a vessel within a tenant, ordered by criterion.
        /// </summary>
        Task<List<VesselHealthOverride>> ReadByVesselAsync(string tenantId, string vesselId, CancellationToken token = default);

        /// <summary>
        /// Delete the override for one criterion of a vessel within a tenant.
        /// </summary>
        Task DeleteAsync(string tenantId, string vesselId, VesselHealthCriterionEnum criterion, CancellationToken token = default);

        /// <summary>
        /// Delete every override for a vessel within a tenant.
        /// </summary>
        Task DeleteByVesselAsync(string tenantId, string vesselId, CancellationToken token = default);
    }
}
