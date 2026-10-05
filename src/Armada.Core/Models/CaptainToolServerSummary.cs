namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// Summary of one MCP server or internal tool source available to a captain runtime.
    /// </summary>
    public class CaptainToolServerSummary
    {
        /// <summary>
        /// Source name, typically the MCP server name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Source kind, such as MCP server or internal runtime support.
        /// </summary>
        public CaptainToolSourceKindEnum SourceKind { get; set; } = CaptainToolSourceKindEnum.McpServer;

        /// <summary>
        /// Transport type, such as stdio or streamable_http.
        /// </summary>
        public string Transport { get; set; } = String.Empty;

        /// <summary>
        /// Target address or command summary for the source.
        /// </summary>
        public string Target { get; set; } = String.Empty;

        /// <summary>
        /// Sanitized HTTP URL for the source when it is network-addressable.
        /// Credentials, query strings, and fragments are removed.
        /// </summary>
        public string? Url { get; set; } = null;

        /// <summary>
        /// Command used to launch the source when it uses stdio transport.
        /// Arguments are intentionally omitted.
        /// </summary>
        public string? Command { get; set; } = null;

        /// <summary>
        /// Working directory configured for the source, when available.
        /// </summary>
        public string? WorkingDirectory { get; set; } = null;

        /// <summary>
        /// Whether the source is enabled in the captain runtime configuration.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Whether Armada successfully reached the source.
        /// </summary>
        public bool Reachable { get; set; } = false;

        /// <summary>
        /// Number of tools returned by the source after filtering.
        /// </summary>
        public int ToolCount { get; set; } = 0;

        /// <summary>
        /// Number of configured HTTP headers associated with the source.
        /// Header values are intentionally not exposed.
        /// </summary>
        public int HeaderCount { get; set; } = 0;

        /// <summary>
        /// Number of configured environment variables associated with the source.
        /// Variable values are intentionally not exposed.
        /// </summary>
        public int EnvironmentVariableCount { get; set; } = 0;

        /// <summary>
        /// Number of explicit allow-list tool filters configured for the source.
        /// </summary>
        public int EnabledToolFilterCount { get; set; } = 0;

        /// <summary>
        /// Number of explicit deny-list tool filters configured for the source.
        /// </summary>
        public int DisabledToolFilterCount { get; set; } = 0;

        /// <summary>
        /// Startup timeout in seconds for the source, when known.
        /// </summary>
        public int StartupTimeoutSeconds { get; set; } = 0;

        /// <summary>
        /// Tool request timeout in seconds for the source, when known.
        /// </summary>
        public int ToolTimeoutSeconds { get; set; } = 0;

        /// <summary>
        /// Human-readable source status.
        /// </summary>
        public string Status { get; set; } = String.Empty;

        /// <summary>
        /// Optional human-readable error when the source could not be inspected.
        /// </summary>
        public string? ErrorMessage { get; set; } = null;
    }
}
