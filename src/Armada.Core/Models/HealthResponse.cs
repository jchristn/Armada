namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response from GET /api/v1/status/health.
    /// </summary>
    public class HealthResponse
    {
        /// <summary>
        /// Health status (e.g. "healthy").
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Current timestamp.
        /// </summary>
        public DateTime? Timestamp { get; set; }

        /// <summary>
        /// Server start time.
        /// </summary>
        public DateTime? StartUtc { get; set; }

        /// <summary>
        /// Uptime as a human-readable string.
        /// </summary>
        public string? Uptime { get; set; }

        /// <summary>
        /// Server version.
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// Port configuration.
        /// </summary>
        public HealthPorts? Ports { get; set; }
    }
}
