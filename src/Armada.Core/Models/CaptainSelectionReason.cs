namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>Typed relational captain_selection_candidate_reasons record; managed fields are never stored as a JSON document.</summary>
    public sealed class CaptainSelectionReason : CaptainSelectionRecord
    {
        /// <summary>DecisionId captured as decision_id in the selection ledger.</summary>
        public string DecisionId { get; set; } = String.Empty;
        /// <summary>CandidateId captured as candidate_id in the selection ledger.</summary>
        public string CandidateId { get; set; } = String.Empty;
        /// <summary>Ordinal captured as ordinal_number in the selection ledger.</summary>
        public int Ordinal { get; set; }
        /// <summary>ReasonCode captured as reason_code in the selection ledger.</summary>
        public string ReasonCode { get; set; } = String.Empty;
        /// <summary>RequiredPercent captured as required_percent in the selection ledger.</summary>
        public double? RequiredPercent { get; set; }
        /// <summary>AvailablePercent captured as available_percent in the selection ledger.</summary>
        public double? AvailablePercent { get; set; }
        /// <summary>BlockerId captured as blocker_id in the selection ledger.</summary>
        public string? BlockerId { get; set; }
        /// <summary>Create an identified CaptainSelectionReason record.</summary>
        public CaptainSelectionReason() : base("csr_") { }
    }
}
