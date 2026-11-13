namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>Pure policy result; does not claim, reserve, persist or launch work.</summary>
    public sealed class UsageSelectionEvaluation
    {
        /// <summary>All evaluated candidates, ordered deterministically.</summary>
        public List<UsageSelectionCandidateResult> Candidates { get; set; } = new List<UsageSelectionCandidateResult>();

        /// <summary>Hypothetical selected captain, or null when blocked.</summary>
        public string? SelectedCaptainId { get; set; } = null;

        /// <summary>Overall semantic outcome.</summary>
        public string ReasonCode { get; set; } = "NoEligibleCaptain";

        /// <summary>Whether the input exceeded the configured cap; no truncated admission is allowed.</summary>
        public bool CandidateLimitExceeded { get; set; } = false;
    }
}
