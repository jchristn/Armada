namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>Sanitized account observation, with no raw diagnostic excerpt.</summary>
    public class UsageSnapshotView
    {
        /// <summary>Id: account usage contract.</summary>
        public string? Id { get; set; }
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
        public List<string> CaptainIds { get; set; } = new List<string>();
        /// <summary>CapturedUtc: account usage contract.</summary>
        public DateTime CapturedUtc { get; set; }
        /// <summary>Status: account usage contract.</summary>
        public UsageSnapshotStatusEnum Status { get; set; }
        /// <summary>ErrorCode: account usage contract.</summary>
        public string? ErrorCode { get; set; }
        /// <summary>Windows: account usage contract.</summary>
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
    }
}
