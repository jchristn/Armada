namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database methods for events.
    /// </summary>
    public interface IEventMethods
    {
        /// <summary>
        /// Create an event.
        /// </summary>
        Task<ArmadaEvent> CreateAsync(ArmadaEvent armadaEvent, CancellationToken token = default);

        /// <summary>
        /// Read an event by identifier.
        /// </summary>
        Task<ArmadaEvent?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Delete an event by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate recent events.
        /// </summary>
        Task<List<ArmadaEvent>> EnumerateRecentAsync(int limit = 50, CancellationToken token = default);

        /// <summary>
        /// Enumerate events filtered by event type.
        /// </summary>
        Task<List<ArmadaEvent>> EnumerateByTypeAsync(string eventType, int limit = 50, CancellationToken token = default);

        /// <summary>
        /// Enumerate events filtered by entity.
        /// </summary>
        Task<List<ArmadaEvent>> EnumerateByEntityAsync(string entityType, string entityId, int limit = 50, CancellationToken token = default);

        /// <summary>
        /// Enumerate events filtered by captain.
        /// </summary>
        Task<List<ArmadaEvent>> EnumerateByCaptainAsync(string captainId, int limit = 50, CancellationToken token = default);

        /// <summary>
        /// Enumerate events filtered by mission.
        /// </summary>
        Task<List<ArmadaEvent>> EnumerateByMissionAsync(string missionId, int limit = 50, CancellationToken token = default);

        /// <summary>
        /// Enumerate events filtered by vessel.
        /// </summary>
        Task<List<ArmadaEvent>> EnumerateByVesselAsync(string vesselId, int limit = 50, CancellationToken token = default);

        /// <summary>
        /// Enumerate events filtered by voyage.
        /// </summary>
        Task<List<ArmadaEvent>> EnumerateByVoyageAsync(string voyageId, int limit = 50, CancellationToken token = default);

        /// <summary>
        /// Enumerate events with pagination and filtering.
        /// </summary>
        Task<EnumerationResult<ArmadaEvent>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Read an event by tenant and identifier (tenant-scoped).
        /// </summary>
        Task<ArmadaEvent?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Delete an event by tenant and identifier (tenant-scoped).
        /// </summary>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all events for a tenant.
        /// </summary>
        Task<List<ArmadaEvent>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Enumerate events with pagination and filtering (tenant-scoped).
        /// </summary>
        Task<EnumerationResult<ArmadaEvent>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Enumerate recent events (tenant-scoped).
        /// </summary>
        Task<List<ArmadaEvent>> EnumerateRecentAsync(string tenantId, int limit = 50, CancellationToken token = default);

        /// <summary>
        /// Enumerate events filtered by tenant and event type (tenant-scoped).
        /// </summary>
        Task<List<ArmadaEvent>> EnumerateByTypeAsync(string tenantId, string eventType, int limit = 50, CancellationToken token = default);

        /// <summary>
        /// Enumerate events filtered by tenant and entity (tenant-scoped).
        /// </summary>
        Task<List<ArmadaEvent>> EnumerateByEntityAsync(string tenantId, string entityType, string entityId, int limit = 50, CancellationToken token = default);

        /// <summary>
        /// Enumerate events filtered by tenant and captain (tenant-scoped).
        /// </summary>
        Task<List<ArmadaEvent>> EnumerateByCaptainAsync(string tenantId, string captainId, int limit = 50, CancellationToken token = default);

        /// <summary>
        /// Enumerate events filtered by tenant and mission (tenant-scoped).
        /// </summary>
        Task<List<ArmadaEvent>> EnumerateByMissionAsync(string tenantId, string missionId, int limit = 50, CancellationToken token = default);

        /// <summary>
        /// Enumerate events filtered by tenant and vessel (tenant-scoped).
        /// </summary>
        Task<List<ArmadaEvent>> EnumerateByVesselAsync(string tenantId, string vesselId, int limit = 50, CancellationToken token = default);

        /// <summary>
        /// Enumerate events filtered by tenant and voyage (tenant-scoped).
        /// </summary>
        Task<List<ArmadaEvent>> EnumerateByVoyageAsync(string tenantId, string voyageId, int limit = 50, CancellationToken token = default);

        /// <summary>
        /// Read an event by tenant, user, and identifier (user-scoped).
        /// </summary>
        Task<ArmadaEvent?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Delete an event by tenant, user, and identifier (user-scoped).
        /// </summary>
        Task DeleteAsync(string tenantId, string userId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate all events owned by a user within a tenant (user-scoped).
        /// </summary>
        Task<List<ArmadaEvent>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>
        /// Enumerate events with pagination and filtering (user-scoped).
        /// </summary>
        Task<EnumerationResult<ArmadaEvent>> EnumerateAsync(string tenantId, string userId, EnumerationQuery query, CancellationToken token = default);
    }
}
