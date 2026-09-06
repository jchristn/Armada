namespace Armada.Core.Harbor
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// Harbor-to-server opening message: identifies the Harbor and advertises what it can run, so the
    /// Admiral can register it and route work by capability and capacity.
    /// </summary>
    public class HarborHandshake : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Harbor identifier (hbr_ prefix).
        /// </summary>
        public string HarborId { get; set; } = string.Empty;

        /// <summary>
        /// Human-facing Harbor name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Protocol version the Harbor speaks.
        /// </summary>
        public string ProtocolVersion { get; set; } = string.Empty;

        /// <summary>
        /// Operating-system platform (for example "Windows", "Linux", "macOS").
        /// </summary>
        public string? OsPlatform { get; set; } = null;

        /// <summary>
        /// Processor architecture (for example "X64", "Arm64").
        /// </summary>
        public string? Architecture { get; set; } = null;

        /// <summary>
        /// Maximum concurrent jobs the Harbor will accept.
        /// </summary>
        public int MaxConcurrentJobs { get; set; } = 4;

        /// <summary>
        /// Advertised capabilities (available runtimes and host tools).
        /// </summary>
        public List<HarborCapability> Capabilities { get; set; } = new List<HarborCapability>();

        #endregion
    }
}
