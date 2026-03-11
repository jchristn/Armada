namespace Armada.Core.Services.Interfaces
{
    using Armada.Core.Enums;

    /// <summary>
    /// Service for evaluating escalation rules and sending notifications.
    /// </summary>
    public interface IEscalationService
    {
        /// <summary>
        /// Evaluate all configured escalation rules against current system state.
        /// Called during each health check cycle.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        Task EvaluateAsync(CancellationToken token = default);

        /// <summary>
        /// Fire an event-based escalation trigger immediately.
        /// </summary>
        /// <param name="trigger">Trigger type.</param>
        /// <param name="entityId">Identifier of the entity that triggered the escalation.</param>
        /// <param name="message">Descriptive message.</param>
        /// <param name="token">Cancellation token.</param>
        Task FireAsync(EscalationTriggerEnum trigger, string entityId, string message, CancellationToken token = default);
    }
}
