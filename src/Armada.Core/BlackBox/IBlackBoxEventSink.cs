namespace Armada.Core.BlackBox
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Where black box events go. The Core implementation (<see cref="DatabaseBlackBoxEventSink"/>) records an ArmadaEvent
    /// with the typed payload as JSON; the Admiral's implementation also broadcasts it to the tenant's WebSocket clients.
    /// Implementations must not throw for delivery problems (log instead) and must be thread-safe.
    /// </summary>
    public interface IBlackBoxEventSink
    {
        /// <summary>
        /// Record and deliver an event.
        /// </summary>
        /// <param name="eventType">Event type (BlackBoxEventTypes).</param>
        /// <param name="message">Human-readable message.</param>
        /// <param name="tenantId">Tenant, or null.</param>
        /// <param name="entryId">Entry, or null.</param>
        /// <param name="missionId">Mission, or null.</param>
        /// <param name="voyageId">Voyage, or null.</param>
        /// <param name="vesselId">Vessel, or null.</param>
        /// <param name="payload">Typed payload (serialized as JSON).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task EmitAsync(string eventType, string message, string? tenantId, string? entryId, string? missionId, string? voyageId, string? vesselId, object payload, CancellationToken token = default);
    }
}
