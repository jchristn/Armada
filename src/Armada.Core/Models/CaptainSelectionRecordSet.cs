namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>An atomic decision aggregate; all managed collections map to relational child rows.</summary>
    public sealed class CaptainSelectionRecordSet
    {
        /// <summary>Internal immutable recovery revision bound to this evaluation. Zero requires no authorization.</summary>
        [JsonIgnore]
        public int AdmissionRecoveryRevision { get; set; }
        /// <summary>Internal compare-and-set mission version for enforcement; never persisted as an opaque record.</summary>
        [JsonIgnore]
        public DateTime? AdmissionMissionVersionUtc { get; set; }
        /// <summary>Internal selected captain configuration/capacity version used during enforcement.</summary>
        [JsonIgnore]
        public DateTime? AdmissionCaptainVersionUtc { get; set; }
        /// <summary>Transient raw model version binding. Never serialized or copied into public evidence without sanitization.</summary>
        [JsonIgnore]
        public string? AdmissionCaptainModel { get; set; }
        /// <summary>Actual credential host selected for this admission; required to use known account evidence in enforcement.</summary>
        [JsonIgnore]
        public string? AdmissionHostId { get; set; }
        /// <summary>
        /// Earlier execution attempt of this mission whose Active or Unsettled reservations an automatic redispatch carries
        /// forward (that attempt produced no work). Null for an ordinary admission.
        /// </summary>
        [JsonIgnore]
        public string? AdmissionCarriedAttemptId { get; set; }
        /// <summary>Capture-enabled policy snapshot used by the shared source normalizer.</summary>
        [JsonIgnore]
        public bool AdmissionCaptureEnabled { get; set; }
        /// <summary>Immutable effective policy revision for the decision.</summary>
        public CaptainSelectionPolicyRevision Policy { get; set; } = new CaptainSelectionPolicyRevision();
        /// <summary>Actual/hypothetical admission or material wait decision.</summary>
        public CaptainSelectionDecision Decision { get; set; } = new CaptainSelectionDecision();
        /// <summary>
        /// Stored candidate evidence: the captains that explain the outcome (selected, proposed, requested), and for a wait the
        /// candidates nearest to eligibility. Every other evaluated candidate is counted in <see cref="OmittedCandidates"/>.
        /// </summary>
        public List<CaptainSelectionCandidateRecord> Candidates { get; set; } = new List<CaptainSelectionCandidateRecord>();
        /// <summary>Structured reasons with typed parameters.</summary>
        public List<CaptainSelectionReason> Reasons { get; set; } = new List<CaptainSelectionReason>();
        /// <summary>Immutable copies of normalized window evidence.</summary>
        public List<CaptainSelectionWindowRecord> Windows { get; set; } = new List<CaptainSelectionWindowRecord>();
        /// <summary>Per-owner, per-reason counts of evaluated candidates not stored individually.</summary>
        public List<CaptainSelectionOmittedCandidates> OmittedCandidates { get; set; } = new List<CaptainSelectionOmittedCandidates>();
        /// <summary>New active or shadow reservations written with the attempt.</summary>
        public List<UsageBudgetReservation> Reservations { get; set; } = new List<UsageBudgetReservation>();
        /// <summary>Execution events initially correlated to this decision.</summary>
        public List<CaptainSelectionExecutionEvent> Events { get; set; } = new List<CaptainSelectionExecutionEvent>();
    }
}
