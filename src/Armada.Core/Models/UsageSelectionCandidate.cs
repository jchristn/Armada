namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>Read-only evaluator input assembled after shared eligibility and routing fences.</summary>
    public sealed class UsageSelectionCandidate
    {
        /// <summary>Captain identifier, used as an ordinal final tie-breaker.</summary>
        public string CaptainId { get; set; } = String.Empty;

        /// <summary>Normalized provider used only for comparable-window tie breaking.</summary>
        public string? Provider { get; set; } = null;

        /// <summary>Proven intended model, or null when automatic routing is unresolved.</summary>
        public string? Model { get; set; } = null;

        /// <summary>Existing routing preference bucket; lower is preferred.</summary>
        public int RoutingBucket { get; set; } = 0;

        /// <summary>Existing safety/capacity/eligibility blocker, or null.</summary>
        public string? IneligibilityReason { get; set; } = null;

        /// <summary>Occupied execution slots at evaluation.</summary>
        public int OccupiedSlots { get; set; } = 0;

        /// <summary>Effective positive captain capacity.</summary>
        public int Capacity { get; set; } = 1;

        /// <summary>Known assignment timestamp; unknown never wins by being zero.</summary>
        public DateTime? LastAssignedUtc { get; set; } = null;

        /// <summary>Whether current effective account membership is verified.</summary>
        public bool IdentityVerified { get; set; } = false;

        /// <summary>Existing quota hold that unknown evidence cannot bypass.</summary>
        public bool HasAuthoritativeQuotaHold { get; set; } = false;

        /// <summary>Classified provider cooldown, if present.</summary>
        public DateTime? CooldownUntilUtc { get; set; } = null;

        /// <summary>Observation timestamp, not completion time of a failed retry.</summary>
        public DateTime? CapturedUtc { get; set; } = null;

        /// <summary>Status of the selected evidence.</summary>
        public UsageSnapshotStatusEnum Status { get; set; } = UsageSnapshotStatusEnum.Failed;

        /// <summary>Evidence source; derived estimates cannot authorize usage.</summary>
        public UsageCaptureSourceEnum Source { get; set; } = UsageCaptureSourceEnum.Derived;

        /// <summary>Capture interval for the existing freshness policy.</summary>
        public int EffectiveCaptureIntervalSeconds { get; set; } = 60;

        /// <summary>Whether evidence belongs to the effective account configuration.</summary>
        public bool ConfigurationMatches { get; set; } = true;

        /// <summary>Whether aged consumption estimates make the account uncertain.</summary>
        public bool HasUncertainDebt { get; set; } = false;

        /// <summary>Applicable-window inputs; null list entries are invalid evidence.</summary>
        public List<UsageSelectionWindowEvidence> Windows { get; set; } = new List<UsageSelectionWindowEvidence>();
    }
}
