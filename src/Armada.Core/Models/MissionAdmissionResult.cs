namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// The typed outcome of the last dispatch attempt for a Pending mission (RS-4.6). Dispatch writes it; the dispatch log,
    /// the assignment blocker, and the stuck-admission monitor read it instead of evaluating admission again.
    /// </summary>
    public sealed class MissionAdmissionResult
    {
        /// <summary>Mission as the attempt last read it.</summary>
        public Mission Mission { get; set; } = null!;

        /// <summary>Why the mission was not admitted. <c>AwaitingDispatch</c> means every gate was open but no claim landed.</summary>
        public MissionAssignmentBlocker Blocker { get; set; } = new MissionAssignmentBlocker();

        /// <summary>When the mission first waited for this reason without interruption.</summary>
        public DateTime FirstObservedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>When the attempt that produced this result ran.</summary>
        public DateTime LastObservedUtc { get; set; } = DateTime.UtcNow;
    }
}
