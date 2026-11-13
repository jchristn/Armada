namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>Deterministic explainable outcome for one candidate; contains no rendered UI text.</summary>
    public sealed class UsageSelectionCandidateResult
    {
        /// <summary>Original candidate evidence; callers must not mutate during evaluation.</summary>
        public UsageSelectionCandidate Candidate { get; set; } = new UsageSelectionCandidate();

        /// <summary>Headroom band for ranking.</summary>
        public UsageSelectionBandEnum Band { get; set; } = UsageSelectionBandEnum.Unknown;

        /// <summary>Stable semantic reason.</summary>
        public string ReasonCode { get; set; } = "UsageMissing";

        /// <summary>Tightest comparable applicable window headroom, including estimates.</summary>
        public double? AvailablePercent { get; set; } = null;

        /// <summary>Reported reset or retry hint, never a guaranteed start time.</summary>
        public DateTime? NextReconsiderUtc { get; set; } = null;

        /// <summary>Signature used only when provider and window semantics are comparable.</summary>
        public string? ComparableWindowKey { get; set; } = null;

        /// <summary>Whether the hypothetical enforced policy permits admission.</summary>
        public bool CanSelect { get; set; } = false;
    }
}
