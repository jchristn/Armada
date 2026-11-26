namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Escalation service that records each fired trigger instead of notifying anyone.
    /// </summary>
    public class RecordingEscalationService : IEscalationService
    {
        /// <summary>Triggers fired, in order.</summary>
        public List<EscalationTriggerEnum> Triggers { get; } = new List<EscalationTriggerEnum>();

        /// <summary>Entity ids fired with, in the same order as <see cref="Triggers"/>.</summary>
        public List<string> EntityIds { get; } = new List<string>();

        /// <summary>Messages fired with, in the same order as <see cref="Triggers"/>.</summary>
        public List<string> Messages { get; } = new List<string>();

        /// <summary>Does nothing.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A completed task.</returns>
        public Task EvaluateAsync(CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>Record the trigger.</summary>
        /// <param name="trigger">Trigger.</param>
        /// <param name="entityId">Entity id.</param>
        /// <param name="message">Message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A completed task.</returns>
        public Task FireAsync(EscalationTriggerEnum trigger, string entityId, string message, CancellationToken token = default)
        {
            Triggers.Add(trigger);
            EntityIds.Add(entityId);
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
