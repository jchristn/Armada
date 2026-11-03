namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>Typed relational captain_selection_windows record; managed fields are never stored as a JSON document.</summary>
    public sealed class CaptainSelectionWindowRecord : CaptainSelectionRecord
    {
        /// <summary>DecisionId captured as decision_id in the selection ledger.</summary>
        public string DecisionId { get; set; } = String.Empty;
        /// <summary>CandidateId captured as candidate_id in the selection ledger.</summary>
        public string CandidateId { get; set; } = String.Empty;
        /// <summary>SnapshotId captured as snapshot_id in the selection ledger.</summary>
        public string? SnapshotId { get; set; }
        /// <summary>CapturedUtc captured as captured_utc in the selection ledger.</summary>
        public DateTime? CapturedUtc { get; set; }
        /// <summary>ProbeStartedUtc captured as probe_started_utc in the selection ledger.</summary>
        public DateTime? ProbeStartedUtc { get; set; }
        /// <summary>Source captured as source in the selection ledger.</summary>
        public string? Source { get; set; }
        /// <summary>Status captured as status in the selection ledger.</summary>
        public string? Status { get; set; }
        /// <summary>ConfigurationRevision captured as configuration_revision in the selection ledger.</summary>
        public string? ConfigurationRevision { get; set; }
        /// <summary>WindowKey captured as window_key in the selection ledger.</summary>
        public string WindowKey { get; set; } = String.Empty;
        /// <summary>WindowName captured as window_name in the selection ledger.</summary>
        public string? WindowName { get; set; }
        /// <summary>Model captured as model in the selection ledger.</summary>
        public string? Model { get; set; }
        /// <summary>Scope captured as scope_name in the selection ledger.</summary>
        public string? Scope { get; set; }
        /// <summary>Generation captured as generation_key in the selection ledger.</summary>
        public string? Generation { get; set; }
        /// <summary>UsedPercent captured as used_percent in the selection ledger.</summary>
        public double? UsedPercent { get; set; }
        /// <summary>Used captured as used_amount in the selection ledger.</summary>
        public long? Used { get; set; }
        /// <summary>Limit captured as limit_amount in the selection ledger.</summary>
        public long? Limit { get; set; }
        /// <summary>Unit captured as unit in the selection ledger.</summary>
        public string? Unit { get; set; }
        /// <summary>ResetsUtc captured as resets_utc in the selection ledger.</summary>
        public DateTime? ResetsUtc { get; set; }
        /// <summary>Applicable captured as applicable in the selection ledger.</summary>
        public bool Applicable { get; set; }
        /// <summary>ActiveReservationPercent captured as active_reservation_percent in the selection ledger.</summary>
        public double ActiveReservationPercent { get; set; }
        /// <summary>UnsettledPercent captured as unsettled_percent in the selection ledger.</summary>
        public double UnsettledPercent { get; set; }
        /// <summary>ReservePercent captured as reserve_percent in the selection ledger.</summary>
        public double ReservePercent { get; set; }
        /// <summary>Create an identified CaptainSelectionWindowRecord record.</summary>
        public CaptainSelectionWindowRecord() : base("csw_") { }
    }
}
