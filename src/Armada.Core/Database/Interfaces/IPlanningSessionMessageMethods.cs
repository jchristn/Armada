namespace Armada.Core.Database.Interfaces
{
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for planning session transcript messages.
    /// </summary>
    public interface IPlanningSessionMessageMethods
    {
        /// <summary>
        /// Create a planning session message.
        /// </summary>
        Task<PlanningSessionMessage> CreateAsync(PlanningSessionMessage message, CancellationToken token = default);

        /// <summary>
        /// Read a planning session message by identifier.
        /// </summary>
        Task<PlanningSessionMessage?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Update a planning session message.
        /// </summary>
        Task<PlanningSessionMessage> UpdateAsync(PlanningSessionMessage message, CancellationToken token = default);

        /// <summary>
        /// Delete a planning session message by identifier.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Delete all planning session messages for a session.
        /// </summary>
        Task DeleteBySessionAsync(string planningSessionId, CancellationToken token = default);

        /// <summary>
        /// Enumerate planning session messages for a session.
        /// </summary>
        Task<List<PlanningSessionMessage>> EnumerateBySessionAsync(string planningSessionId, CancellationToken token = default);

        /// <summary>
        /// Read one page of a session's transcript. CreatedAscending is oldest first (by sequence), CreatedDescending
        /// (the default) newest first; the id breaks ties. The page is cut in the database.
        /// </summary>
        /// <param name="planningSessionId">Planning session identifier.</param>
        /// <param name="query">Page number, page size (1 to 1000), and order.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of messages.</returns>
        Task<EnumerationResult<PlanningSessionMessage>> EnumeratePageBySessionAsync(string planningSessionId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Count the messages of a session's transcript.
        /// </summary>
        /// <param name="planningSessionId">Planning session identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of messages.</returns>
        Task<long> CountBySessionAsync(string planningSessionId, CancellationToken token = default);
    }
}
