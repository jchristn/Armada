namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>Read-only current-state evaluation. A preview never writes history, reserves or probes usage.</summary>
    public sealed class CaptainSelectionPreview
    {
        /// <summary>Mission whose current inputs were evaluated.</summary>
        public string MissionId { get; set; } = String.Empty;
        /// <summary>Always true for this non-admitting response.</summary>
        public bool IsPreview { get; set; } = true;
        /// <summary>Current rollout mode.</summary>
        public UsageSelectionModeEnum Mode { get; set; }
        /// <summary>Configured selection strategy.</summary>
        public CaptainSelectionStrategyEnum Strategy { get; set; }
        /// <summary>Policy algorithm used.</summary>
        public string AlgorithmVersion { get; set; } = String.Empty;
        /// <summary>Evaluation time.</summary>
        public DateTime EvaluatedUtc { get; set; }
        /// <summary>Existing routing choice, when current safety gates permit selection.</summary>
        public string? ActualCaptainId { get; set; }
        /// <summary>Hypothetical usage-aware selection, when qualified and permitted.</summary>
        public string? HypotheticalCaptainId { get; set; }
        /// <summary>Stable summary reason.</summary>
        public string ReasonCode { get; set; } = String.Empty;
        /// <summary>One page of caller-visible candidate explanations.</summary>
        public EnumerationResult<CaptainSelectionCandidateView> Candidates { get; set; } = new EnumerationResult<CaptainSelectionCandidateView>();
    }
}
