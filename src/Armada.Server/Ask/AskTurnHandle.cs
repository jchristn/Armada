namespace Armada.Server.Ask
{
    using System;
    using System.Threading;

    /// <summary>
    /// A captain turn running in an Ask Armada thread (at most one per thread).
    /// </summary>
    public class AskTurnHandle
    {
        #region Public-Members

        /// <summary>
        /// Turn identifier stamped on ask.* events.
        /// </summary>
        public string TurnId { get; } = "trn_" + Guid.NewGuid().ToString("N");

        /// <summary>
        /// Thread identifier.
        /// </summary>
        public string ThreadId { get; }

        /// <summary>
        /// Captain running the turn, or null for a captain-less turn (deterministic summary).
        /// </summary>
        public string? CaptainId { get; }

        /// <summary>
        /// Turn kind: Message, FollowUp, or Summary.
        /// </summary>
        public string Kind { get; }

        /// <summary>
        /// Cancellation source; cancelling stops the captain's process.
        /// </summary>
        public CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();

        /// <summary>
        /// UTC start time.
        /// </summary>
        public DateTime StartedUtc { get; } = DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="captainId">Captain identifier, or null.</param>
        /// <param name="kind">Turn kind.</param>
        /// <exception cref="ArgumentNullException">Thrown when threadId is null or empty.</exception>
        public AskTurnHandle(string threadId, string? captainId, string kind)
        {
            if (String.IsNullOrEmpty(threadId)) throw new ArgumentNullException(nameof(threadId));
            ThreadId = threadId;
            CaptainId = captainId;
            Kind = String.IsNullOrEmpty(kind) ? "Message" : kind;
        }

        #endregion
    }
}
