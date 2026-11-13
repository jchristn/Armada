namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>Typed relational captain_selection_decisions record; managed fields are never stored as a JSON document.</summary>
    public sealed class CaptainSelectionDecision : CaptainSelectionRecord
    {
        /// <summary>MissionId captured as mission_id in the selection ledger.</summary>
        public string MissionId { get; set; } = String.Empty;
        /// <summary>VoyageId captured as voyage_id in the selection ledger.</summary>
        public string? VoyageId { get; set; }
        /// <summary>ExecutionAttemptId captured as execution_attempt_id in the selection ledger.</summary>
        public string ExecutionAttemptId { get; set; } = String.Empty;
        /// <summary>Trigger captured as trigger_kind in the selection ledger.</summary>
        public string Trigger { get; set; } = String.Empty;
        /// <summary>Mode captured as mode in the selection ledger.</summary>
        public UsageSelectionModeEnum Mode { get; set; }
        /// <summary>Outcome captured as outcome in the selection ledger.</summary>
        public string Outcome { get; set; } = String.Empty;
        /// <summary>SelectedCaptainId captured as selected_captain_id in the selection ledger.</summary>
        public string? SelectedCaptainId { get; set; }
        /// <summary>SelectedProvider captured as selected_provider in the selection ledger.</summary>
        public string? SelectedProvider { get; set; }
        /// <summary>SelectedModel captured as selected_model in the selection ledger.</summary>
        public string? SelectedModel { get; set; }
        /// <summary>HypotheticalCaptainId captured as hypothetical_captain_id in the selection ledger.</summary>
        public string? HypotheticalCaptainId { get; set; }
        /// <summary>HypotheticalOutcome captured as hypothetical_outcome in the selection ledger.</summary>
        public string? HypotheticalOutcome { get; set; }
        /// <summary>AlgorithmVersion captured as algorithm_version in the selection ledger.</summary>
        public string AlgorithmVersion { get; set; } = String.Empty;
        /// <summary>PolicyRevisionId captured as policy_revision_id in the selection ledger.</summary>
        public string PolicyRevisionId { get; set; } = String.Empty;
        /// <summary>RoutingRevision captured as routing_revision in the selection ledger.</summary>
        public string? RoutingRevision { get; set; }
        /// <summary>EvidenceFingerprint captured as evidence_fingerprint in the selection ledger.</summary>
        public string EvidenceFingerprint { get; set; } = String.Empty;
        /// <summary>PreviousDecisionId captured as previous_decision_id in the selection ledger.</summary>
        public string? PreviousDecisionId { get; set; }
        /// <summary>EvaluatedUtc captured as evaluated_utc in the selection ledger.</summary>
        public DateTime EvaluatedUtc { get; set; }
        /// <summary>NextReconsiderUtc captured as next_reconsider_utc in the selection ledger.</summary>
        public DateTime? NextReconsiderUtc { get; set; }
        /// <summary>PrimaryReasonCode captured as primary_reason_code in the selection ledger.</summary>
        public string PrimaryReasonCode { get; set; } = String.Empty;
        /// <summary>OverrideActorId captured as override_actor_id in the selection ledger.</summary>
        public string? OverrideActorId { get; set; }
        /// <summary>OverrideReason captured as override_reason in the selection ledger.</summary>
        public string? OverrideReason { get; set; }
        /// <summary>CandidateCount captured as candidate_count in the selection ledger.</summary>
        public int CandidateCount { get; set; }
        /// <summary>Create an identified CaptainSelectionDecision record.</summary>
        public CaptainSelectionDecision() : base("csd_") { }
    }
}
