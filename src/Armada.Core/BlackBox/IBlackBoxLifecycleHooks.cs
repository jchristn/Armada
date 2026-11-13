namespace Armada.Core.BlackBox
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Calls the delete and update paths make after they change a black box subject, so retention cascades and expiry
    /// recomputes happen at once instead of at the next garbage collection run (which also catches anything missed).
    /// Implementations never throw for store or database failures; they log and leave the work to the next run.
    /// </summary>
    public interface IBlackBoxLifecycleHooks
    {
        /// <summary>
        /// A mission row was deleted (purge_mission, delete_missions, DELETE /api/v1/missions/{id}/purge): its entries are
        /// deleted when blackBox.retention.deleteWithMission is true, and kept (with the denormalized title) otherwise.
        /// </summary>
        /// <param name="missionId">Mission id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task OnMissionDeletedAsync(string missionId, CancellationToken token = default);

        /// <summary>
        /// A vessel row was deleted: its entries are deleted when blackBox.retention.deleteWithVessel is true.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task OnVesselDeletedAsync(string vesselId, CancellationToken token = default);

        /// <summary>
        /// A tenant is being deleted: every entry and object of the tenant is deleted, pinned entries included.
        /// </summary>
        /// <param name="tenantId">Tenant id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task OnTenantDeletedAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// A vessel was updated (its blackBoxRetentionDays override may have changed): its entries' expiry is recomputed
        /// in the background.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task OnVesselUpdatedAsync(string vesselId, CancellationToken token = default);
    }
}
