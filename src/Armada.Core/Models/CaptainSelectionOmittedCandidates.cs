namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Typed relational captain_selection_omitted_candidates record: how many evaluated candidates owned by one user
    /// shared a primary reason but were not stored as individual candidate rows.
    /// </summary>
    public sealed class CaptainSelectionOmittedCandidates : CaptainSelectionRecord
    {
        /// <summary>DecisionId captured as decision_id in the selection ledger.</summary>
        public string DecisionId { get; set; } = String.Empty;
        /// <summary>ReasonCode captured as reason_code in the selection ledger.</summary>
        public string ReasonCode { get; set; } = String.Empty;
        /// <summary>CandidateCount captured as candidate_count in the selection ledger.</summary>
        public int CandidateCount { get; set; }
        /// <summary>Create an identified CaptainSelectionOmittedCandidates record.</summary>
        public CaptainSelectionOmittedCandidates() : base("cso_") { }
    }
}
