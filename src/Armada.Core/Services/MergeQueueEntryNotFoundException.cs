namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Raised when a landing handoff in merge queue mode returns without recording a merge queue entry for the mission.
    /// Derives from <see cref="InvalidOperationException"/> so existing handlers keep working.
    /// </summary>
    public class MergeQueueEntryNotFoundException : InvalidOperationException
    {
        /// <summary>
        /// Mission whose merge queue entry is missing.
        /// </summary>
        public string MissionId { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="missionId">Mission whose merge queue entry is missing.</param>
        public MergeQueueEntryNotFoundException(string missionId)
            : base("Landing handoff returned without recording a merge queue entry.")
        {
            MissionId = missionId ?? String.Empty;
        }
    }
}
