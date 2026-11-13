namespace Armada.Core.Models
{
    using Armada.Core.Services;
    /// <summary>
    /// Machine-readable endpoint metadata returned by `mux endpoint list/show --output-format json`.
    /// </summary>
    public class MuxEndpointInfo
    {
        /// <summary>
        /// Endpoint name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Adapter type.
        /// </summary>
        public string AdapterType { get; set; } = String.Empty;

        /// <summary>
        /// Base URL.
        /// </summary>
        public string BaseUrl { get; set; } = String.Empty;

        /// <summary>
        /// Model identifier.
        /// </summary>
        public string Model { get; set; } = String.Empty;

        /// <summary>
        /// Whether this is the default endpoint.
        /// </summary>
        public bool IsDefault { get; set; } = false;

        /// <summary>
        /// Maximum output tokens.
        /// </summary>
        public int MaxTokens { get; set; } = 0;

        /// <summary>
        /// Sampling temperature.
        /// </summary>
        public double Temperature { get; set; } = 0;

        /// <summary>
        /// Context window size.
        /// </summary>
        public int ContextWindow { get; set; } = 0;

        /// <summary>
        /// Timeout in milliseconds.
        /// </summary>
        public int TimeoutMs { get; set; } = 0;

        /// <summary>
        /// Whether tool calling is enabled.
        /// </summary>
        public bool ToolsEnabled { get; set; } = false;

        /// <summary>
        /// Header names configured for the endpoint.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> HeaderNames { get; set; } = new List<string>();

        /// <summary>
        /// Header values with secrets redacted.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    }
}
