namespace Test.Shared.Infrastructure
{
    using System;

    /// <summary>
    /// An owner-scoped Ask Armada event captured by <see cref="AskTestHarness"/>.
    /// </summary>
    public sealed class AskRecordedEvent
    {
        /// <summary>
        /// Tenant the event was addressed to.
        /// </summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>
        /// User the event was addressed to.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Event type (ask.message, ask.thread, ask.proposal, ask.work, ...).
        /// </summary>
        public string EventType { get; set; } = String.Empty;

        /// <summary>
        /// Payload.
        /// </summary>
        public object? Payload { get; set; } = null;
    }
}
