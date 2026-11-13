namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>Typed relational usage_budget_reservations record; managed fields are never stored as a JSON document.</summary>
    public sealed class UsageBudgetReservation : CaptainSelectionRecord
    {
        /// <summary>DecisionId captured as decision_id in the selection ledger.</summary>
        public string DecisionId { get; set; } = String.Empty;
        /// <summary>MissionId captured as mission_id in the selection ledger.</summary>
        public string MissionId { get; set; } = String.Empty;
        /// <summary>ExecutionAttemptId captured as execution_attempt_id in the selection ledger.</summary>
        public string ExecutionAttemptId { get; set; } = String.Empty;
        /// <summary>CaptainId captured as captain_id in the selection ledger.</summary>
        public string CaptainId { get; set; } = String.Empty;
        /// <summary>MembershipId captured as membership_id in the selection ledger.</summary>
        public string MembershipId { get; set; } = String.Empty;
        /// <summary>HostId captured as host_id in the selection ledger.</summary>
        public string HostId { get; set; } = String.Empty;
        /// <summary>Runtime captured as runtime in the selection ledger.</summary>
        public string Runtime { get; set; } = String.Empty;
        /// <summary>AccountKey captured as account_key in the selection ledger.</summary>
        public string AccountKey { get; set; } = String.Empty;
        /// <summary>WindowKey captured as window_key in the selection ledger.</summary>
        public string WindowKey { get; set; } = String.Empty;
        /// <summary>Generation captured as generation_key in the selection ledger.</summary>
        public string Generation { get; set; } = String.Empty;
        /// <summary>ReservedPercent captured as reserved_percent in the selection ledger.</summary>
        public double ReservedPercent { get; set; }
        /// <summary>State captured as state in the selection ledger.</summary>
        public UsageReservationStateEnum State { get; set; }
        /// <summary>Revision captured as revision in the selection ledger.</summary>
        public int Revision { get; set; }
        /// <summary>ExecutionStartedUtc captured as execution_started_utc in the selection ledger.</summary>
        public DateTime? ExecutionStartedUtc { get; set; }
        /// <summary>ExecutionEndedUtc captured as execution_ended_utc in the selection ledger.</summary>
        public DateTime? ExecutionEndedUtc { get; set; }
        /// <summary>SettlementEligibleUtc captured as settlement_eligible_utc in the selection ledger.</summary>
        public DateTime? SettlementEligibleUtc { get; set; }
        /// <summary>SettledBySnapshotId captured as settled_by_snapshot_id in the selection ledger.</summary>
        public string? SettledBySnapshotId { get; set; }
        /// <summary>ReleasedUtc captured as released_utc in the selection ledger.</summary>
        public DateTime? ReleasedUtc { get; set; }
        /// <summary>TransitionReason captured as transition_reason in the selection ledger.</summary>
        public string? TransitionReason { get; set; }
        /// <summary>Create an identified UsageBudgetReservation record.</summary>
        public UsageBudgetReservation() : base("ubr_") { }
    }
}
