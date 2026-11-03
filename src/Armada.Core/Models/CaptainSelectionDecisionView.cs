namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Settings;

    /// <summary>Authorized immutable selection summary with intended versus reported model provenance.</summary>
    public sealed class CaptainSelectionDecisionView
    {
        /// <summary>Decision identity.</summary>
        public string Id { get; set; } = String.Empty;
        /// <summary>Authorized tenant scope for subsequent detail requests.</summary>
        public string TenantId { get; set; } = String.Empty;
        /// <summary>Mission reference, which may outlive a deleted mission.</summary>
        public string MissionId { get; set; } = String.Empty;
        /// <summary>Actual attempt correlation only for an admitted decision.</summary>
        public string? ExecutionAttemptId { get; set; } = null;
        /// <summary>Dispatch, repair or material waiting trigger.</summary>
        public string Trigger { get; set; } = String.Empty;
        /// <summary>Admitted or Waiting outcome.</summary>
        public string Outcome { get; set; } = String.Empty;
        /// <summary>Mode used by the decision.</summary>
        public UsageSelectionModeEnum Mode { get; set; } = UsageSelectionModeEnum.Observe;
        /// <summary>Configured strategy used at that time.</summary>
        public CaptainSelectionStrategyEnum Strategy { get; set; } = CaptainSelectionStrategyEnum.FirstAvailable;
        /// <summary>Actual captain selected by the running mode.</summary>
        public string? SelectedCaptainId { get; set; } = null;
        /// <summary>Historical authorized captain name, when retained.</summary>
        public string? SelectedCaptainName { get; set; } = null;
        /// <summary>Configured model at selection; never presented as an actual report.</summary>
        public string? IntendedModel { get; set; } = null;
        /// <summary>Model explicitly reported by the runtime.</summary>
        public string? ReportedModel { get; set; } = null;
        /// <summary>Origin of the actual-model report.</summary>
        public string? ModelReportingSource { get; set; } = null;
        /// <summary>Hypothetical configured-strategy choice in Observe mode.</summary>
        public string? HypotheticalCaptainId { get; set; } = null;
        /// <summary>Stable semantic explanation.</summary>
        public string ReasonCode { get; set; } = String.Empty;
        /// <summary>Policy algorithm identity.</summary>
        public string AlgorithmVersion { get; set; } = String.Empty;
        /// <summary>Immutable effective policy reference.</summary>
        public string PolicyRevisionId { get; set; } = String.Empty;
        /// <summary>Decision evaluation time.</summary>
        public DateTime EvaluatedUtc { get; set; } = DateTime.MinValue;
        /// <summary>Recorded reset/retry hint, never a start guarantee.</summary>
        public DateTime? NextReconsiderUtc { get; set; } = null;
    }
}
