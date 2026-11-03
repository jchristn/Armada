namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Settings;

    /// <summary>Current attempt correlation and latest retained decision for a visible mission.</summary>
    public sealed class CaptainSelectionSummary
    {
        /// <summary>Authorized mission identity.</summary>
        public string MissionId { get; set; } = String.Empty;
        /// <summary>Current rollout mode.</summary>
        public UsageSelectionModeEnum CurrentMode { get; set; } = UsageSelectionModeEnum.Observe;
        /// <summary>Current configured strategy.</summary>
        public CaptainSelectionStrategyEnum CurrentStrategy { get; set; } = CaptainSelectionStrategyEnum.FirstAvailable;
        /// <summary>Whether any retained decision exists.</summary>
        public bool HistoryAvailable { get; set; } = false;
        /// <summary>Decision for the current admitted attempt; null for an unrecorded attempt.</summary>
        public CaptainSelectionDecisionView? CurrentAttempt { get; set; } = null;
        /// <summary>Latest retained admission or material wait decision.</summary>
        public CaptainSelectionDecisionView? LatestDecision { get; set; } = null;
    }
}
