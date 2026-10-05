namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// Identifier fields of a /ws event payload (entity change events and Ask Armada thread events).
    /// </summary>
    public class E2eWebSocketEventData
    {
        #region Public-Members

        /// <summary>
        /// Entity id (entity change events such as voyage.changed).
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Entity status (entity change events).
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Ask thread id (ask.* events).
        /// </summary>
        public string? ThreadId { get; set; } = null;

        /// <summary>
        /// Tracked work id (ask.work).
        /// </summary>
        public string? TrackedWorkId { get; set; } = null;

        /// <summary>
        /// Proposal (ask.proposal).
        /// </summary>
        public E2eWebSocketEntityRef? Proposal { get; set; } = null;

        /// <summary>
        /// Thread (ask.thread).
        /// </summary>
        public E2eWebSocketEntityRef? Thread { get; set; } = null;

        #endregion
    }
}
