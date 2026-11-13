namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>Typed relational captain_selection_execution_events record; managed fields are never stored as a JSON document.</summary>
    public sealed class CaptainSelectionExecutionEvent : CaptainSelectionRecord
    {
        /// <summary>DecisionId captured as decision_id in the selection ledger.</summary>
        public string DecisionId { get; set; } = String.Empty;
        /// <summary>ExecutionAttemptId captured as execution_attempt_id in the selection ledger.</summary>
        public string ExecutionAttemptId { get; set; } = String.Empty;
        /// <summary>EventKind captured as event_kind in the selection ledger.</summary>
        public string EventKind { get; set; } = String.Empty;
        /// <summary>ActualProvider captured as actual_provider in the selection ledger.</summary>
        public string? ActualProvider { get; set; }
        /// <summary>ActualModel captured as actual_model in the selection ledger.</summary>
        public string? ActualModel { get; set; }
        /// <summary>ReportingSource captured as reporting_source in the selection ledger.</summary>
        public string? ReportingSource { get; set; }
        /// <summary>OccurredUtc captured as occurred_utc in the selection ledger.</summary>
        public DateTime OccurredUtc { get; set; }
        /// <summary>FailureClassification captured as failure_classification in the selection ledger.</summary>
        public string? FailureClassification { get; set; }
        /// <summary>Create an identified CaptainSelectionExecutionEvent record.</summary>
        public CaptainSelectionExecutionEvent() : base("cse_") { }
    }
}
