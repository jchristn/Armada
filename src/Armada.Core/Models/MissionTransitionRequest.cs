namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One mission status transition for <see cref="Armada.Core.Services.MissionTransitions"/>: the statuses the stored
    /// row may have, the target status, and the other fields to change in the same write.
    /// </summary>
    public class MissionTransitionRequest
    {
        /// <summary>
        /// Statuses the stored mission must still have for the write to apply. A status with no legal edge to
        /// <see cref="To"/> is dropped; when none is left the transition is refused.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<MissionStatusEnum> AllowedFrom { get; set; } = new List<MissionStatusEnum>();

        /// <summary>
        /// Target status.
        /// </summary>
        public MissionStatusEnum To { get; set; } = MissionStatusEnum.Pending;

        /// <summary>
        /// Changes to the mission's other fields, applied before the write. Null for none.
        /// </summary>
        public Action<Mission>? Mutate { get; set; } = null;

        /// <summary>
        /// When set, the write also requires the stored row's last update time to equal this value, so a write based on
        /// a stale read is refused even when the status still matches.
        /// </summary>
        public DateTime? ExpectedLastUpdateUtc { get; set; } = null;

        /// <summary>
        /// Message of the <c>mission.status_changed</c> event; null for a default naming the status and title.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Whether an operator asked for the change. Operator transitions are checked against
        /// <see cref="Armada.Core.Services.MissionStateMachine.IsValidTransition"/>; Armada's own against
        /// <see cref="Armada.Core.Services.MissionStateMachine.IsValidInternalTransition"/>.
        /// </summary>
        public bool Operator { get; set; } = false;

        /// <summary>
        /// Whether to re-evaluate the voyage after a move to a terminal status. False when the caller settles the voyage
        /// itself (for example a voyage cancellation).
        /// </summary>
        public bool ReevaluateVoyage { get; set; } = true;
    }
}
