namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for the per-vessel health row. The row's status columns hold effective
    /// (override-aware) values written by the evaluator. Deleting a vessel deletes its health row.
    /// </summary>
    public interface IVesselHealthMethods
    {
        /// <summary>
        /// Insert or replace the health row for health.VesselId (one row per vessel). An existing row keeps its
        /// identifier and CreatedUtc; a new row backfills an empty identifier. LastUpdateUtc is set to now.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when health is null.</exception>
        /// <exception cref="ArgumentException">Thrown when VesselId or TenantId is empty.</exception>
        Task<VesselHealth> UpsertAsync(VesselHealth health, CancellationToken token = default);

        /// <summary>
        /// Read the health row for a vessel within a tenant (with VesselName, FleetId, and FleetName populated), or
        /// null when the vessel has not been evaluated or is not in the tenant.
        /// </summary>
        Task<VesselHealth?> ReadByVesselAsync(string tenantId, string vesselId, CancellationToken token = default);

        /// <summary>
        /// Delete the health row for a vessel within a tenant.
        /// </summary>
        Task DeleteByVesselAsync(string tenantId, string vesselId, CancellationToken token = default);

        /// <summary>
        /// Enumerate vessels in a tenant with their health, filtered, sorted, and paged entirely in SQL. Every
        /// vessel matching the filters appears, including vessels never evaluated (null Id, Unknown statuses).
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when tenantId is empty.</exception>
        Task<EnumerationResult<VesselHealth>> EnumerateAsync(string tenantId, VesselHealthEnumerateRequest request, CancellationToken token = default);

        /// <summary>
        /// Count active vessels in a tenant by effective overall status, plus not-evaluated, outdated-major, and
        /// high-or-critical-vulnerability vessel counts.
        /// </summary>
        Task<VesselHealthSummary> CountByOverallStatusAsync(string tenantId, CancellationToken token = default);
    }
}
