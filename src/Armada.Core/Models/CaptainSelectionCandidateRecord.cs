namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>Typed relational captain_selection_candidates record; managed fields are never stored as a JSON document.</summary>
    public sealed class CaptainSelectionCandidateRecord : CaptainSelectionRecord
    {
        /// <summary>DecisionId captured as decision_id in the selection ledger.</summary>
        public string DecisionId { get; set; } = String.Empty;
        /// <summary>CaptainId captured as captain_id in the selection ledger.</summary>
        public string CaptainId { get; set; } = String.Empty;
        /// <summary>Provider captured as provider in the selection ledger.</summary>
        public string? Provider { get; set; }
        /// <summary>ConfiguredModel captured as configured_model in the selection ledger.</summary>
        public string? ConfiguredModel { get; set; }
        /// <summary>RoutingBucket captured as routing_bucket in the selection ledger.</summary>
        public int RoutingBucket { get; set; }
        /// <summary>Rank captured as rank_number in the selection ledger.</summary>
        public int Rank { get; set; }
        /// <summary>Band captured as band in the selection ledger.</summary>
        public UsageSelectionBandEnum Band { get; set; }
        /// <summary>PrimaryReasonCode captured as primary_reason_code in the selection ledger.</summary>
        public string PrimaryReasonCode { get; set; } = String.Empty;
        /// <summary>Eligible captured as eligible in the selection ledger.</summary>
        public bool Eligible { get; set; }
        /// <summary>OccupiedSlots captured as occupied_slots in the selection ledger.</summary>
        public int OccupiedSlots { get; set; }
        /// <summary>Capacity captured as capacity in the selection ledger.</summary>
        public int Capacity { get; set; }
        /// <summary>LastAssignedUtc captured as last_assigned_utc in the selection ledger.</summary>
        public DateTime? LastAssignedUtc { get; set; }
        /// <summary>MembershipId captured as membership_id in the selection ledger.</summary>
        public string? MembershipId { get; set; }
        /// <summary>HostId captured as host_id in the selection ledger.</summary>
        public string? HostId { get; set; }
        /// <summary>Runtime captured as runtime in the selection ledger.</summary>
        public string? Runtime { get; set; }
        /// <summary>AccountKey captured as account_key in the selection ledger.</summary>
        public string? AccountKey { get; set; }
        /// <summary>AvailablePercent captured as available_percent in the selection ledger.</summary>
        public double? AvailablePercent { get; set; }
        /// <summary>Historical authorized display name; null on older decision records.</summary>
        public string? CaptainName { get; set; }

        /// <summary>Capture time used by the original evaluation, even if its raw snapshot expires.</summary>
        public DateTime? EvidenceCapturedUtc { get; set; }

        /// <summary>Transient current-visibility signal from a scoped query, excluded from stored evidence.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool CurrentCaptainExists { get; set; }

        /// <summary>Create an identified CaptainSelectionCandidateRecord record.</summary>
        public CaptainSelectionCandidateRecord() : base("csc_") { }
    }
}
