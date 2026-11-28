namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for missions.
    /// </summary>
    public interface IMissionMethods
    {
        /// <summary>Count execution slots in one snapshot without loading outputs or diffs.</summary>
        Task<int> CountExecutionSlotsAsync(CancellationToken token = default);

        /// <summary>
        /// Create a mission.
        /// </summary>
        Task<Mission> CreateAsync(Mission mission, CancellationToken token = default);

        /// <summary>
        /// Create several missions and move other missions' dependencies onto them in one transaction, so a multi-row
        /// change (a Judge rework chain: the rework, its repeated stages, the new reviewer, and the re-pointed later
        /// stages) is either written completely or not at all.
        /// </summary>
        /// <param name="created">Missions to create, in order.</param>
        /// <param name="repointed">Dependency changes to apply after the missions are created.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        Task CreateChainAsync(IReadOnlyList<Mission> created, IReadOnlyList<MissionDependencyUpdate> repointed, CancellationToken token = default);

        /// <summary>
        /// Write a batch of mission rows in one transaction: the created missions, then each guarded update, then the
        /// playbook snapshots. When any guarded update finds the stored status changed, nothing is written.
        /// </summary>
        /// <param name="batch">The rows to write.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the whole batch was written; false when a guard failed and nothing was written.</returns>
        /// <exception cref="ArgumentNullException">The batch is null.</exception>
        Task<bool> WriteBatchAsync(MissionWriteBatch batch, CancellationToken token = default);

        /// <summary>
        /// Read a mission by identifier.
        /// </summary>
        Task<Mission?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a lightweight mission summary by identifier.
        /// </summary>
        Task<MissionSummary?> ReadSummaryAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Update a mission.
        /// </summary>
        Task<Mission> UpdateAsync(Mission mission, CancellationToken token = default);

        /// <summary>
        /// Write every column of the mission only while its stored status is one of <paramref name="expectedStatuses"/>
        /// (a compare-and-set on status). Use it for a transition another party may race, such as a process exit
        /// handler and a completion handler both moving the same mission.
        /// </summary>
        /// <param name="mission">Mission carrying the values to write.</param>
        /// <param name="expectedStatuses">Statuses the stored row must still have for the write to apply.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the row was written; false when its status had already moved on or the mission is missing.</returns>
        Task<bool> TryUpdateIfStatusAsync(Mission mission, IReadOnlyCollection<MissionStatusEnum> expectedStatuses, CancellationToken token = default);

        /// <summary>Write only when both status and the observed row timestamp still match.</summary>
        Task<bool> TryUpdateIfUnchangedAsync(Mission mission, DateTime expectedLastUpdateUtc, IReadOnlyCollection<MissionStatusEnum> expectedStatuses, CancellationToken token = default);


        /// <summary>
        /// Claim ownership of one agent process exit for a mission: clears the recorded process id only while it is still
        /// <paramref name="processId"/>. Exactly one caller wins for a given process, so a process exit is handled once
        /// even when the exit callback and the health check observe it together, and an exit from an earlier attempt can
        /// never act on a later attempt that recorded a different process.
        /// </summary>
        /// <param name="missionId">Mission identifier.</param>
        /// <param name="processId">Process id whose exit the caller wants to handle.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the caller now owns the exit; false when another handler owns it or the mission moved to another process.</returns>
        Task<bool> TryClaimProcessExitAsync(string missionId, int processId, CancellationToken token = default);

        /// <summary>
        /// Record (or clear) the exact start time of the local agent process launched for a mission, together with the
        /// process id it belongs to. Only this method writes the pair; full mission updates leave it unchanged. Clear it
        /// (both null) for a launch that is not a local process, so an earlier local record can never vouch for it.
        /// </summary>
        /// <param name="missionId">Mission identifier.</param>
        /// <param name="processId">Process id the start time belongs to, or null to clear.</param>
        /// <param name="startedUtc">Exact UTC process start time, or null to clear.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the mission row was updated.</returns>
        /// <exception cref="ArgumentNullException">Thrown when missionId is null or empty.</exception>
        Task<bool> SetProcessLaunchAsync(string missionId, int? processId, DateTime? startedUtc, CancellationToken token = default);

        /// <summary>
        /// Update the mission heartbeat timestamp without rewriting the full record.
        /// Implementations should also advance the parent voyage LastUpdateUtc when applicable.
        /// </summary>
        Task UpdateHeartbeatAsync(string id, CancellationToken token = default);

        /// <summary>Update heartbeat and recovery budget only while the recorded execution owner/process still match.</summary>
        Task<bool> TryUpdateRuntimeContextAsync(string id, string captainId, int? processId, DateTime? heartbeatUtc, int recoveryAttempts, CancellationToken token = default);

        /// <summary>
        /// Delete a mission by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all missions.
        /// </summary>
        Task<List<Mission>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate missions with pagination and filtering.
        /// </summary>
        Task<EnumerationResult<Mission>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate lightweight mission summaries with pagination and filtering.
        /// </summary>
        Task<EnumerationResult<MissionSummary>> EnumerateSummariesAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate missions by voyage identifier.
        /// </summary>
        Task<List<Mission>> EnumerateByVoyageAsync(string voyageId, CancellationToken token = default);

        /// <summary>
        /// Enumerate lightweight mission summaries by voyage identifier.
        /// </summary>
        Task<List<MissionSummary>> EnumerateSummariesByVoyageAsync(string voyageId, CancellationToken token = default);

        /// <summary>
        /// Enumerate missions by vessel identifier.
        /// </summary>
        Task<List<Mission>> EnumerateByVesselAsync(string vesselId, CancellationToken token = default);

        /// <summary>Read only the indexed vessel's scheduling metadata, excluding outputs, diffs and evidence.</summary>
        Task<List<MissionAdmissionSnapshot>> EnumerateAdmissionSnapshotsAsync(string vesselId, CancellationToken token = default);

        /// <summary>
        /// Read the missions running on a vessel (Assigned, InProgress, Verifying) for its admission locks. Only Id, Title,
        /// Description, Status, BranchName, VoyageId and VesselId are populated; outputs, diffs and evidence are not read.
        /// </summary>
        /// <exception cref="ArgumentNullException">vesselId is null or empty.</exception>
        Task<List<Mission>> EnumerateRunningOnVesselAsync(string vesselId, CancellationToken token = default);

        /// <summary>
        /// Enumerate lightweight mission summaries by vessel identifier.
        /// </summary>
        Task<List<MissionSummary>> EnumerateSummariesByVesselAsync(string vesselId, CancellationToken token = default);

        /// <summary>
        /// Enumerate missions by captain identifier.
        /// </summary>
        Task<List<Mission>> EnumerateByCaptainAsync(string captainId, CancellationToken token = default);

        /// <summary>
        /// Enumerate lightweight mission summaries by captain identifier.
        /// </summary>
        Task<List<MissionSummary>> EnumerateSummariesByCaptainAsync(string captainId, CancellationToken token = default);

        /// <summary>
        /// Enumerate missions by status.
        /// </summary>
        Task<List<Mission>> EnumerateByStatusAsync(MissionStatusEnum status, CancellationToken token = default);

        /// <summary>
        /// Enumerate missions in a status whose last update is at or after a cutoff, so a periodic sweep reads only
        /// recent rows instead of the whole status (RS-10.2). Ordered by priority, then creation time.
        /// </summary>
        /// <param name="status">Mission status.</param>
        /// <param name="updatedSince">Inclusive lower bound on <see cref="Mission.LastUpdateUtc"/> (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Matching missions.</returns>
        Task<List<Mission>> EnumerateByStatusAsync(MissionStatusEnum status, DateTime updatedSince, CancellationToken token = default);

        /// <summary>
        /// Count missions grouped by status.
        /// </summary>
        Task<Dictionary<MissionStatusEnum, int>> CountByStatusAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate lightweight mission history points in the supplied time range.
        /// </summary>
        Task<List<MissionHistoryPoint>> EnumerateHistoryPointsAsync(MissionHistoryQuery query, CancellationToken token = default);

        /// <summary>
        /// Count missions by history bucket and status in the database (one row per bucket and status that has
        /// missions), over the query's window, vessel, and fleet filters. The bucket index counts whole buckets from
        /// <see cref="MissionHistoryQuery.AlignedStartUtc"/>.
        /// </summary>
        Task<List<MissionHistoryBucketCount>> CountHistoryBucketsAsync(MissionHistoryQuery query, CancellationToken token = default);

        /// <summary>
        /// Check if a mission exists by identifier.
        /// </summary>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a mission by tenant and identifier (tenant-scoped).
        /// </summary>
        Task<Mission?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a lightweight mission summary by tenant and identifier (tenant-scoped).
        /// </summary>
        Task<MissionSummary?> ReadSummaryAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Delete a mission by tenant and identifier (tenant-scoped).
        /// </summary>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all missions in a tenant (tenant-scoped).
        /// </summary>
        Task<List<Mission>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Enumerate missions with pagination and filtering (tenant-scoped).
        /// </summary>
        Task<EnumerationResult<Mission>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate lightweight mission summaries with pagination and filtering (tenant-scoped).
        /// </summary>
        Task<EnumerationResult<MissionSummary>> EnumerateSummariesAsync(string tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate missions by tenant and voyage identifier (tenant-scoped).
        /// </summary>
        Task<List<Mission>> EnumerateByVoyageAsync(string tenantId, string voyageId, CancellationToken token = default);

        /// <summary>
        /// Enumerate lightweight mission summaries by tenant and voyage identifier (tenant-scoped).
        /// </summary>
        Task<List<MissionSummary>> EnumerateSummariesByVoyageAsync(string tenantId, string voyageId, CancellationToken token = default);

        /// <summary>
        /// Enumerate missions by tenant and vessel identifier (tenant-scoped).
        /// </summary>
        Task<List<Mission>> EnumerateByVesselAsync(string tenantId, string vesselId, CancellationToken token = default);

        /// <summary>
        /// Enumerate lightweight mission summaries by tenant and vessel identifier (tenant-scoped).
        /// </summary>
        Task<List<MissionSummary>> EnumerateSummariesByVesselAsync(string tenantId, string vesselId, CancellationToken token = default);

        /// <summary>
        /// Enumerate missions by tenant and captain identifier (tenant-scoped).
        /// </summary>
        Task<List<Mission>> EnumerateByCaptainAsync(string tenantId, string captainId, CancellationToken token = default);

        /// <summary>
        /// Enumerate lightweight mission summaries by tenant and captain identifier (tenant-scoped).
        /// </summary>
        Task<List<MissionSummary>> EnumerateSummariesByCaptainAsync(string tenantId, string captainId, CancellationToken token = default);

        /// <summary>
        /// Enumerate missions by tenant and status (tenant-scoped).
        /// </summary>
        Task<List<Mission>> EnumerateByStatusAsync(string tenantId, MissionStatusEnum status, CancellationToken token = default);

        /// <summary>
        /// Count tenant-scoped missions grouped by status.
        /// </summary>
        Task<Dictionary<MissionStatusEnum, int>> CountByStatusAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Enumerate lightweight tenant-scoped mission history points in the supplied time range.
        /// </summary>
        Task<List<MissionHistoryPoint>> EnumerateHistoryPointsAsync(string tenantId, MissionHistoryQuery query, CancellationToken token = default);

        /// <summary>
        /// Count tenant-scoped missions by history bucket and status in the database (one row per bucket and status that has
        /// missions), over the query's window, vessel, and fleet filters. The bucket index counts whole buckets from
        /// <see cref="MissionHistoryQuery.AlignedStartUtc"/>.
        /// </summary>
        Task<List<MissionHistoryBucketCount>> CountHistoryBucketsAsync(string tenantId, MissionHistoryQuery query, CancellationToken token = default);

        /// <summary>
        /// Check if a mission exists by tenant and identifier (tenant-scoped).
        /// </summary>
        Task<bool> ExistsAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a mission by tenant, user, and identifier (user-scoped).
        /// </summary>
        Task<Mission?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a lightweight mission summary by tenant, user, and identifier (user-scoped).
        /// </summary>
        Task<MissionSummary?> ReadSummaryAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Delete a mission by tenant, user, and identifier (user-scoped).
        /// </summary>
        Task DeleteAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all missions owned by a user within a tenant (user-scoped).
        /// </summary>
        Task<List<Mission>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>
        /// Enumerate missions with pagination and filtering (user-scoped).
        /// </summary>
        Task<EnumerationResult<Mission>> EnumerateAsync(string tenantId, string userId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate lightweight mission summaries with pagination and filtering (user-scoped).
        /// </summary>
        Task<EnumerationResult<MissionSummary>> EnumerateSummariesAsync(string tenantId, string userId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Count user-scoped missions grouped by status.
        /// </summary>
        Task<Dictionary<MissionStatusEnum, int>> CountByStatusAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>
        /// Enumerate lightweight user-scoped mission history points in the supplied time range.
        /// </summary>
        Task<List<MissionHistoryPoint>> EnumerateHistoryPointsAsync(string tenantId, string userId, MissionHistoryQuery query, CancellationToken token = default);

        /// <summary>
        /// Count user-scoped missions by history bucket and status in the database (one row per bucket and status that has
        /// missions), over the query's window, vessel, and fleet filters. The bucket index counts whole buckets from
        /// <see cref="MissionHistoryQuery.AlignedStartUtc"/>.
        /// </summary>
        Task<List<MissionHistoryBucketCount>> CountHistoryBucketsAsync(string tenantId, string userId, MissionHistoryQuery query, CancellationToken token = default);
    }
}
