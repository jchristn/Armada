namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Result of GET /api/v1/status/health (unauthenticated).
    /// </summary>
    public class HealthResult
    {
        #region Public-Members

        /// <summary>
        /// Health status, for example healthy.
        /// </summary>
        public string Status { get; set; } = "";

        /// <summary>
        /// Server time.
        /// </summary>
        public DateTime? Timestamp { get; set; } = null;

        /// <summary>
        /// Server start time.
        /// </summary>
        public DateTime? StartUtc { get; set; } = null;

        /// <summary>
        /// Uptime as d.hh:mm:ss.
        /// </summary>
        public string? Uptime { get; set; } = null;

        /// <summary>
        /// Server version.
        /// </summary>
        public string? Version { get; set; } = null;

        /// <summary>
        /// Ports.
        /// </summary>
        public Armada.Core.Models.HealthPorts? Ports { get; set; } = null;

        /// <summary>
        /// Remote tunnel status, or null.
        /// </summary>
        public Armada.Core.Models.RemoteTunnelStatus? RemoteTunnel { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HealthResult()
        {
        }

        #endregion
    }
}
