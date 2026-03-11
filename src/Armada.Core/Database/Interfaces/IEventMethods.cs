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
    }
}
