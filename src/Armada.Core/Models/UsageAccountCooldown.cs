namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>Typed relational usage_account_cooldowns record; managed fields are never stored as a JSON document.</summary>
    public sealed class UsageAccountCooldown : CaptainSelectionRecord
    {
        /// <summary>Actual execution receipt; null for older uncorrelated cooldown records.</summary>
        public string? ExecutionKey { get; set; }
        /// <summary>Mission whose owned process exit contributed this cooldown.</summary>
        public string? MissionId { get; set; }
        /// <summary>Immutable decision, absent for Disabled/legacy work.</summary>
        public string? DecisionId { get; set; }
        /// <summary>HostId captured as host_id in the selection ledger.</summary>
        public string HostId { get; set; } = String.Empty;
        /// <summary>Runtime captured as runtime in the selection ledger.</summary>
        public string Runtime { get; set; } = String.Empty;
        /// <summary>AccountKey captured as account_key in the selection ledger.</summary>
        public string AccountKey { get; set; } = String.Empty;
        /// <summary>WindowKey captured as window_key in the selection ledger.</summary>
        public string WindowKey { get; set; } = String.Empty;
        /// <summary>Revision captured as revision in the selection ledger.</summary>
        public int Revision { get; set; }
        /// <summary>ObservedUtc captured as observed_utc in the selection ledger.</summary>
        public DateTime ObservedUtc { get; set; }
        /// <summary>RetryAfterUtc captured as retry_after_utc in the selection ledger.</summary>
        public DateTime RetryAfterUtc { get; set; }
        /// <summary>Classification captured as classification in the selection ledger.</summary>
        public string Classification { get; set; } = String.Empty;
        /// <summary>Create an identified UsageAccountCooldown record.</summary>
        public UsageAccountCooldown() : base("ucc_") { }
    }
}
