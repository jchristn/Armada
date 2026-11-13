namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for planning sessions.
    /// </summary>
    public interface IPlanningSessionMethods
    {
        /// <summary>
        /// Create a planning session.
        /// </summary>
        Task<PlanningSession> CreateAsync(PlanningSession session, CancellationToken token = default);

        /// <summary>
        /// Read a planning session by identifier.
        /// </summary>
        Task<PlanningSession?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Update a planning session.
        /// </summary>
        Task<PlanningSession> UpdateAsync(PlanningSession session, CancellationToken token = default);

        /// <summary>
        /// Delete a planning session by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all planning sessions.
        /// </summary>
        Task<List<PlanningSession>> EnumerateAsync(CancellationToken token = default);

        /// <summary>
        /// Enumerate planning sessions reserved by a captain.
        /// </summary>
        Task<List<PlanningSession>> EnumerateByCaptainAsync(string captainId, CancellationToken token = default);

        /// <summary>
        /// Enumerate planning sessions by status.
        /// </summary>
        Task<List<PlanningSession>> EnumerateByStatusAsync(PlanningSessionStatusEnum status, CancellationToken token = default);

        /// <summary>
        /// Read a planning session by tenant and identifier.
        /// </summary>
        Task<PlanningSession?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate planning sessions for a tenant.
        /// </summary>
        Task<List<PlanningSession>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Read a planning session by tenant, user, and identifier.
        /// </summary>
        Task<PlanningSession?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate planning sessions for a user within a tenant.
        /// </summary>
        Task<List<PlanningSession>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>
        /// Read one page of planning sessions, most recently updated first for CreatedDescending (the default) and
        /// least recently updated first for CreatedAscending; the id breaks ties. Scope and filters apply before the
        /// count. Filters: Status, CaptainId, VesselId, FleetId, CreatedAfter, CreatedBefore.
        /// </summary>
        /// <param name="tenantId">Only this tenant's sessions, or null for every tenant.</param>
        /// <param name="userId">Only this user's sessions, or null for every user.</param>
        /// <param name="query">Paging, order, and filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of sessions.</returns>
        Task<EnumerationResult<PlanningSession>> EnumeratePageAsync(string? tenantId, string? userId, EnumerationQuery query, CancellationToken token = default);
    }
}
