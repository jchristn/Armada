namespace Armada.Core.Services
{
    using Armada.Core.Models;
    /// <summary>
    /// Machine-readable result returned by `mux endpoint list --output-format json`.
    /// </summary>
    public class MuxEndpointListResult
    {
        /// <summary>
        /// Structured output contract version.
        /// </summary>
        public int ContractVersion { get; set; } = 0;

        /// <summary>
        /// Harbor whose mux answered, or null when mux ran on the Admiral host. Set by the Admiral, not by mux.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Whether the command succeeded.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Effective config directory used for inspection.
        /// </summary>
        public string ConfigDirectory { get; set; } = String.Empty;

        /// <summary>
        /// Machine-readable error code when the command fails.
        /// </summary>
        public string ErrorCode { get; set; } = String.Empty;

        /// <summary>
        /// Human-readable error message when the command fails.
        /// </summary>
        public string ErrorMessage { get; set; } = String.Empty;

        /// <summary>
        /// Configured endpoints.
        /// </summary>
        public List<MuxEndpointInfo> Endpoints { get; set; } = new List<MuxEndpointInfo>();
    }
}
