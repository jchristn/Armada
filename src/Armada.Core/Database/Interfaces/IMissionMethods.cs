namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for missions.
    /// </summary>
    public interface IMissionMethods
    {
        /// <summary>
        /// Create a mission.
        /// </summary>
        Task<Mission> CreateAsync(Mission mission, CancellationToken token = default);

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
        /// Update the mission heartbeat timestamp without rewriting the full record.
        /// Implementations should also advance the parent voyage LastUpdateUtc when applicable.
        /// </summary>
        Task UpdateHeartbeatAsync(string id, CancellationToken token = default);

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
        /// Count missions grouped by status.
        /// </summary>
        Task<Dictionary<MissionStatusEnum, int>> CountByStatusAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate lightweight mission history points in the supplied time range.
        /// </summary>
        Task<List<MissionHistoryPoint>> EnumerateHistoryPointsAsync(MissionHistoryQuery query, CancellationToken token = default);

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
    }
}
