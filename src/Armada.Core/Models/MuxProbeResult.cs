namespace Armada.Core.Models
{
    /// <summary>
    /// Machine-readable result returned by `mux probe --output-format json`.
    /// </summary>
    public class MuxProbeResult
    {
        /// <summary>
        /// Structured output contract version.
        /// </summary>
        public int ContractVersion { get; set; } = 0;

        /// <summary>
        /// Whether the probe succeeded.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Effective endpoint name.
        /// </summary>
        public string EndpointName { get; set; } = String.Empty;

        /// <summary>
        /// Effective adapter type.
        /// </summary>
        public string AdapterType { get; set; } = String.Empty;

        /// <summary>
        /// Effective base URL.
        /// </summary>
        public string BaseUrl { get; set; } = String.Empty;

        /// <summary>
        /// Effective model identifier.
        /// </summary>
        public string Model { get; set; } = String.Empty;

        /// <summary>
        /// Probe response preview when successful.
        /// </summary>
        public string ResponsePreview { get; set; } = String.Empty;

        /// <summary>
        /// Command mode used for the probe.
        /// </summary>
        public string CommandName { get; set; } = String.Empty;

        /// <summary>
        /// Effective mux configuration directory.
        /// </summary>
        public string ConfigDirectory { get; set; } = String.Empty;

        /// <summary>
        /// How mux selected the effective endpoint.
        /// </summary>
        public string EndpointSelectionSource { get; set; } = String.Empty;

        /// <summary>
        /// Whether endpoints.json exists in the active config directory.
        /// </summary>
        public bool EndpointsFilePresent { get; set; } = false;

        /// <summary>
        /// Whether settings.json exists in the active config directory.
        /// </summary>
        public bool SettingsFilePresent { get; set; } = false;

        /// <summary>
        /// Whether mcp-servers.json exists in the active config directory.
        /// </summary>
        public bool McpServersFilePresent { get; set; } = false;

        /// <summary>
        /// Whether built-in tool calling is enabled for the selected endpoint.
        /// </summary>
        public bool ToolsEnabled { get; set; } = false;

        /// <summary>
        /// Number of built-in tools compiled into mux.
        /// </summary>
        public int BuiltInToolCount { get; set; } = 0;

        /// <summary>
        /// Number of tools effectively available for this endpoint.
        /// </summary>
        public int EffectiveToolCount { get; set; } = 0;

        /// <summary>
        /// Whether this command mode supports MCP integration.
        /// </summary>
        public bool McpSupported { get; set; } = false;

        /// <summary>
        /// Whether MCP servers are configured in the active config directory.
        /// </summary>
        public bool McpConfigured { get; set; } = false;

        /// <summary>
        /// Number of configured MCP servers.
        /// </summary>
        public int McpServerCount { get; set; } = 0;

        /// <summary>
        /// Whether the caller required tool support for this probe.
        /// </summary>
        public bool RequireTools { get; set; } = false;

        /// <summary>
        /// Machine-readable error code when the probe fails.
        /// </summary>
        public string ErrorCode { get; set; } = String.Empty;

        /// <summary>
        /// Machine-readable failure category when the probe fails.
        /// </summary>
        public string FailureCategory { get; set; } = String.Empty;

        /// <summary>
        /// Human-readable error message when the probe fails.
        /// </summary>
        public string ErrorMessage { get; set; } = String.Empty;

        /// <summary>
        /// Elapsed duration in milliseconds.
        /// </summary>
        public long DurationMs { get; set; } = 0;
    }
}
