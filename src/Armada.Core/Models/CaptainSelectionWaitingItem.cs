namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// A Pending mission whose latest captain-selection decision is a Waiting state that has lasted past a threshold.
    /// Carries identifiers and the stable reason only; no candidates or account identity.
    /// </summary>
    public sealed class CaptainSelectionWaitingItem
    {
        /// <summary>Mission tenant.</summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>Mission owner, or null.</summary>
        public string? UserId { get; set; } = null;

        /// <summary>Mission identifier.</summary>
        public string MissionId { get; set; } = String.Empty;

        /// <summary>Mission title.</summary>
        public string MissionTitle { get; set; } = String.Empty;

        /// <summary>Latest (waiting) decision identifier.</summary>
        public string DecisionId { get; set; } = String.Empty;

        /// <summary>Stable primary reason code of the waiting decision.</summary>
        public string ReasonCode { get; set; } = String.Empty;

        /// <summary>When the current waiting state was first recorded (material changes create a new decision).</summary>
        public DateTime WaitingSinceUtc { get; set; } = DateTime.UtcNow;
    }
}
