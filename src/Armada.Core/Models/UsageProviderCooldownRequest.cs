namespace Armada.Core.Models
{
    using System;

    /// <summary>Internal owned-exit request. The database resolves account identity from immutable execution evidence.</summary>
    public sealed class UsageProviderCooldownRequest
    {
        /// <summary>Tenant read from the already-authorized mission.</summary>
        public string TenantId { get; set; } = String.Empty;
        /// <summary>Mission whose process exit was claimed by the caller.</summary>
        public string MissionId { get; set; } = String.Empty;
        /// <summary>Captain associated with that owned exit.</summary>
        public string CaptainId { get; set; } = String.Empty;
        /// <summary>Durable execution key, not an owner-provided choice or account key.</summary>
        public string ExecutionKey { get; set; } = String.Empty;
        /// <summary>UTC structured-error observation.</summary>
        public DateTime ObservedUtc { get; set; }
        /// <summary>Bounded retry deadline.</summary>
        public DateTime UntilUtc { get; set; }
    }
}
