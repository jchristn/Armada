namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>Operational uncertainty for an execution without a complete usage reservation. This is not a reconstructed historical selection.</summary>
    public sealed class UsageUntrackedExecution : CaptainSelectionRecord
    {
        /// <summary>Mission owning the execution.</summary>
        public string MissionId { get; set; } = String.Empty;
        /// <summary>Internal durable execution correlation, changed on every admission or repair.</summary>
        public string ExecutionKey { get; set; } = String.Empty;
        /// <summary>Captain that may have consumed allowance.</summary>
        public string CaptainId { get; set; } = String.Empty;
        /// <summary>Verified credential host, or null when attribution was unknown.</summary>
        public string? HostId { get; set; }
        /// <summary>Runtime namespace captured at adoption.</summary>
        public string Runtime { get; set; } = String.Empty;
        /// <summary>Verified internal account identity, or null. Never returned by selection APIs.</summary>
        public string? AccountKey { get; set; }
        /// <summary>True only when captured membership matched the actual execution credential host.</summary>
        public bool AccountAttributionVerified { get; set; }
        /// <summary>Capture configuration revision at adoption, when known.</summary>
        public string? ConfigurationRevision { get; set; }
        /// <summary>Active, Unsettled, Uncertain or Settled accounting lifecycle.</summary>
        public UsageReservationStateEnum State { get; set; } = UsageReservationStateEnum.Active;
        /// <summary>Optimistic reconciliation version.</summary>
        public int Revision { get; set; }
        /// <summary>Conservatively observed execution end.</summary>
        public DateTime? ExecutionEndedUtc { get; set; }
        /// <summary>Capture lag applied to this execution.</summary>
        public int SettlementLagSeconds { get; set; } = 60;
        /// <summary>Age after which missing post-execution evidence becomes uncertain.</summary>
        public int MaximumUnsettledSeconds { get; set; } = 3600;
        /// <summary>Immutable capture proving absorption, when known.</summary>
        public string? SettledBySnapshotId { get; set; }
        /// <summary>Semantic lifecycle reason.</summary>
        public string? TransitionReason { get; set; }
        /// <summary>Create a typed operational debt identity.</summary>
        public UsageUntrackedExecution() : base("uue_") { }
    }
}
