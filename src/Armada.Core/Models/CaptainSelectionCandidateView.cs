namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>Authorized selection explanation without internal account keys or peer mission identities.</summary>
    public sealed class CaptainSelectionCandidateView
    {
        /// <summary>Visible captain identity.</summary>
        public string CaptainId { get; set; } = String.Empty;
        /// <summary>Whether the caller can currently open the captain. False for deleted or no-longer-visible captains.</summary>
        public bool CaptainAvailable { get; set; }
        /// <summary>Visible captain display name.</summary>
        public string CaptainName { get; set; } = String.Empty;
        /// <summary>Configured intended model; null is unknown, never the actual reported model.</summary>
        public string? Model { get; set; }
        /// <summary>Candidate's recorded or proposed band.</summary>
        public UsageSelectionBandEnum Band { get; set; }
        /// <summary>Stable localizable explanation code.</summary>
        public string ReasonCode { get; set; } = String.Empty;
        /// <summary>Whether the usage policy permits this qualified candidate.</summary>
        public bool CanSelect { get; set; }
        /// <summary>Tightest effective headroom, separately labeled from observed provider utilization.</summary>
        public double? AvailablePercent { get; set; }
        /// <summary>Reported reset/retry hint; not a guaranteed start time.</summary>
        public DateTime? NextReconsiderUtc { get; set; }
        /// <summary>Observation time used by this decision, if known.</summary>
        public DateTime? CapturedUtc { get; set; }
        /// <summary>Position in the deterministic full comparison.</summary>
        public int Rank { get; set; }
        /// <summary>True when legacy routing currently chooses this captain.</summary>
        public bool ActualSelection { get; set; }
        /// <summary>True when the configured hypothetical usage strategy chooses this captain.</summary>
        public bool HypotheticalSelection { get; set; }
    }
}
