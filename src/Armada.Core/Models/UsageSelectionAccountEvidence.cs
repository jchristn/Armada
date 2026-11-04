namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>Internal scoped source evidence; account keys must never be returned by selection APIs.</summary>
    public sealed class UsageSelectionAccountEvidence
    {
        /// <summary>Persisted shared-account cooldown; expiry never invents balance.</summary>
        public DateTime? CooldownUntilUtc { get; set; }
        /// <summary>Captain whose effective membership supplied this observation.</summary>
        public string CaptainId { get; set; } = String.Empty;
        /// <summary>Current membership identity.</summary>
        public string MembershipId { get; set; } = String.Empty;
        /// <summary>Stable credential-host identity.</summary>
        public string HostId { get; set; } = String.Empty;
        /// <summary>Runtime namespace of the account.</summary>
        public AgentRuntimeEnum Runtime { get; set; }
        /// <summary>Opaque tenant/host account identity, for internal accounting only.</summary>
        public string AccountKey { get; set; } = String.Empty;
        /// <summary>Descriptive provider, when reported.</summary>
        public string? Provider { get; set; }
        /// <summary>Membership configuration revision.</summary>
        public string? ConfigurationRevision { get; set; }
        /// <summary>Captured configuration context.</summary>
        public string? CaptureContextKey { get; set; }
        /// <summary>Latest good snapshot, if retained.</summary>
        public string? SnapshotId { get; set; }
        /// <summary>Good evidence capture time; error retries do not advance it.</summary>
        public DateTime? CapturedUtc { get; set; }
        /// <summary>Trusted active probe start, if available.</summary>
        public DateTime? ProbeStartedUtc { get; set; }
        /// <summary>Current membership opening time.</summary>
        public DateTime EffectiveFromUtc { get; set; }
        /// <summary>Good source status, or Failed when absent.</summary>
        public UsageSnapshotStatusEnum Status { get; set; } = UsageSnapshotStatusEnum.Failed;
        /// <summary>Good source classification; missing source is conservatively Derived.</summary>
        public UsageCaptureSourceEnum Source { get; set; } = UsageCaptureSourceEnum.Derived;
        /// <summary>Bounded normalized source windows, not a managed selection record.</summary>
        public string? WindowsJson { get; set; }
        /// <summary>Effective account capture interval in seconds.</summary>
        public int EffectiveIntervalSeconds { get; set; } = 60;
    }
}
