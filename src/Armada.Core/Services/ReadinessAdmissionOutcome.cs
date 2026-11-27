namespace Armada.Core.Services
{
    using Armada.Core.Models;

    /// <summary>What the readiness admission gate found for a Pending mission.</summary>
    internal sealed class ReadinessAdmissionOutcome
    {
        /// <summary>The ready outcome: nothing blocks the mission.</summary>
        internal static readonly ReadinessAdmissionOutcome Ready = new ReadinessAdmissionOutcome(null, null);

        /// <summary>Why the mission is not ready, or null when it is.</summary>
        internal MissionAssignmentBlocker? Blocker { get; }

        /// <summary>A failed mission in another slice that ends this integration mission, or null.</summary>
        internal Mission? FailedSlice { get; }

        /// <summary>Instantiate.</summary>
        /// <param name="blocker">Why the mission is not ready, or null.</param>
        /// <param name="failedSlice">A failed mission in another slice, or null.</param>
        internal ReadinessAdmissionOutcome(MissionAssignmentBlocker? blocker, Mission? failedSlice = null)
        {
            Blocker = blocker;
            FailedSlice = failedSlice;
        }
    }
}
