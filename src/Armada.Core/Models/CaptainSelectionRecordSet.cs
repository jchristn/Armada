namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>An atomic decision aggregate; all managed collections map to relational child rows.</summary>
    public sealed class CaptainSelectionRecordSet
    {
        /// <summary>Immutable effective policy revision for the decision.</summary>
        public CaptainSelectionPolicyRevision Policy { get; set; } = new CaptainSelectionPolicyRevision();
        /// <summary>Actual/hypothetical admission or material wait decision.</summary>
        public CaptainSelectionDecision Decision { get; set; } = new CaptainSelectionDecision();
        /// <summary>Complete evaluated candidate evidence.</summary>
        public List<CaptainSelectionCandidateRecord> Candidates { get; set; } = new List<CaptainSelectionCandidateRecord>();
        /// <summary>Structured reasons with typed parameters.</summary>
        public List<CaptainSelectionReason> Reasons { get; set; } = new List<CaptainSelectionReason>();
        /// <summary>Immutable copies of normalized window evidence.</summary>
        public List<CaptainSelectionWindowRecord> Windows { get; set; } = new List<CaptainSelectionWindowRecord>();
        /// <summary>New active or shadow reservations written with the attempt.</summary>
        public List<UsageBudgetReservation> Reservations { get; set; } = new List<UsageBudgetReservation>();
        /// <summary>Execution events initially correlated to this decision.</summary>
        public List<CaptainSelectionExecutionEvent> Events { get; set; } = new List<CaptainSelectionExecutionEvent>();
    }
}
