namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>Sanitized account observation, with no raw diagnostic excerpt.</summary>
    public class UsageSnapshotView
    {
        /// <summary>Id: account usage contract.</summary>
        public string? Id { get; set; }
        /// <summary>Stable persisted account-state identifier, when this is a latest-state view.</summary>
        public string? AccountId { get; set; }
        /// <summary>TenantId: account usage contract.</summary>
        public string? TenantId { get; set; }
        /// <summary>HostId: account usage contract.</summary>
        public string? HostId { get; set; }
        /// <summary>HarborId: account usage contract.</summary>
        public string? HarborId { get; set; }
        /// <summary>Runtime: account usage contract.</summary>
        public AgentRuntimeEnum Runtime { get; set; }
        /// <summary>Provider: account usage contract.</summary>
        public string? Provider { get; set; }
        /// <summary>AccountKey: account usage contract.</summary>
        public string? AccountKey { get; set; }
        /// <summary>CaptainIds: account usage contract.</summary>
        [CollectionBound(100)]
        public List<string> CaptainIds { get; set; } = new List<string>();
        /// <summary>Display names of <see cref="CaptainIds"/>, in the same order; null for a captain that no longer exists. Latest view only.</summary>
        [CollectionBound(100)]
        public List<string?>? CaptainNames { get; set; }
        /// <summary>CapturedUtc: account usage contract.</summary>
        public DateTime CapturedUtc { get; set; }
        /// <summary>Status: account usage contract.</summary>
        public UsageSnapshotStatusEnum Status { get; set; }
        /// <summary>ErrorCode: account usage contract.</summary>
        public string? ErrorCode { get; set; }
        /// <summary>Windows: account usage contract.</summary>
        [CollectionBound(32)]
        public List<UsageWindow> Windows { get; set; } = new List<UsageWindow>();
        /// <summary>Stale: account usage contract.</summary>
        public bool Stale { get; set; }
        /// <summary>ObservedUtc: account usage contract.</summary>
        public DateTime? ObservedUtc { get; set; }
        /// <summary>RequestedIntervalSeconds: account usage contract.</summary>
        public int RequestedIntervalSeconds { get; set; } = 300;
        /// <summary>EffectiveIntervalSeconds: account usage contract.</summary>
        public int EffectiveIntervalSeconds { get; set; } = 300;
        /// <summary>CapabilityReason: account usage contract.</summary>
        public string? CapabilityReason { get; set; }
        /// <summary>
        /// Armada's estimated reservations per observed window (latest reads only), kept separate from observed usage.
        /// Empty when nothing is reserved or estimated.
        /// </summary>
        [CollectionBound(32)]
        public List<UsageReservationEstimate> ReservationEstimates { get; set; } = new List<UsageReservationEstimate>();
        /// <summary>
        /// True when the account has estimated consumption that could not be settled or attributed (uncertain debt or an
        /// untracked execution); selection treats the account's evidence as uncertain until a fresh capture settles it.
        /// </summary>
        public bool ReservationUncertain { get; set; }
    }
}
