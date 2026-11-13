namespace Armada.Core.Models
{
    using System;

    /// <summary>Internal immutable attribution captured with the actual admission, never a public account identifier.</summary>
    public sealed class UsageExecutionSource
    {
        /// <summary>Durable execution identity.</summary>
        public string ExecutionKey { get; set; } = String.Empty;
        /// <summary>Owning tenant.</summary>
        public string TenantId { get; set; } = String.Empty;
        /// <summary>Owning mission.</summary>
        public string MissionId { get; set; } = String.Empty;
        /// <summary>Actual captain.</summary>
        public string CaptainId { get; set; } = String.Empty;
        /// <summary>Recorded decision, absent for Disabled/legacy work.</summary>
        public string? DecisionId { get; set; }
        /// <summary>Actual execution host, not a hypothetical capture host.</summary>
        public string? HostId { get; set; }
        /// <summary>Actual runtime namespace.</summary>
        public string Runtime { get; set; } = String.Empty;
        /// <summary>Verified account only when capture and actual execution host/configuration match.</summary>
        public string? AccountKey { get; set; }
        /// <summary>Verified source configuration.</summary>
        public string? ConfigurationRevision { get; set; }
        /// <summary>Admission timestamp.</summary>
        public DateTime CreatedUtc { get; set; }
        /// <summary>Proven-unstarted release, permanently preventing a later launch of this receipt.</summary>
        public DateTime? ReleasedUtc { get; set; }
        /// <summary>Durable boundary after which execution may have consumed allowance.</summary>
        public DateTime? LaunchAttemptedUtc { get; set; }
    }
}
