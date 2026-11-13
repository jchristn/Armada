namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Payload of the <c>mission.selection_waiting</c> event: the decision and its stable reason. No candidates,
    /// usage percentages or account identity; open the decision for authorized evidence.
    /// </summary>
    public sealed class CaptainSelectionWaitingEvent
    {
        /// <summary>Waiting decision identifier.</summary>
        public string DecisionId { get; set; } = String.Empty;

        /// <summary>Rollout mode of the decision.</summary>
        public UsageSelectionModeEnum Mode { get; set; } = UsageSelectionModeEnum.Observe;

        /// <summary>Stable primary reason code.</summary>
        public string ReasonCode { get; set; } = String.Empty;

        /// <summary>Recheck hint, or null; never a guaranteed start.</summary>
        public DateTime? NextReconsiderUtc { get; set; } = null;
    }
}
