namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Health of a black box storage target.
    /// </summary>
    public class BlackBoxStoreHealth
    {
        /// <summary>
        /// Target.
        /// </summary>
        public BlackBoxStorageTargetEnum Target { get; set; } = BlackBoxStorageTargetEnum.Database;

        /// <summary>
        /// Whether the target is usable.
        /// </summary>
        public bool Healthy { get; set; } = false;

        /// <summary>
        /// Detail or error (no secrets).
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Check latency in milliseconds.
        /// </summary>
        public long LatencyMs { get; set; } = 0;

        /// <summary>
        /// When checked (UTC).
        /// </summary>
        public DateTime CheckedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxStoreHealth()
        {
        }
    }
}
