namespace Armada.Server.RuntimeTools
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One MCP server as configured for a captain runtime (read from the runtime's CLI or config file), normalized
    /// so it can be described and probed for its tool inventory.
    /// </summary>
    public class RuntimeMcpServerDefinition
    {
        #region Public-Members

        /// <summary>
        /// Server name as configured.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// True when the runtime has this server enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Normalized transport: "stdio", "streamable_http", or the configured value when unrecognized.
        /// </summary>
        public string TransportType { get; set; } = String.Empty;

        /// <summary>
        /// Endpoint URL for HTTP transports, or null.
        /// </summary>
        public string? Url { get; set; } = null;

        /// <summary>
        /// Command for the stdio transport, or null.
        /// </summary>
        public string? Command { get; set; } = null;

        /// <summary>
        /// Command arguments for the stdio transport.
        /// </summary>
        public List<string> Arguments { get; set; } = new List<string>();

        /// <summary>
        /// Working directory for the stdio transport, or null.
        /// </summary>
        public string? WorkingDirectory { get; set; } = null;

        /// <summary>
        /// Environment variables for the stdio transport, or null.
        /// </summary>
        public Dictionary<string, string>? Environment { get; set; } = null;

        /// <summary>
        /// HTTP headers for HTTP transports.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Allow-list of tool names; empty means all tools.
        /// </summary>
        public List<string> EnabledTools { get; set; } = new List<string>();

        /// <summary>
        /// Deny-list of tool names.
        /// </summary>
        public List<string> DisabledTools { get; set; } = new List<string>();

        /// <summary>
        /// Time allowed for the server to start.
        /// </summary>
        public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Time allowed for tool discovery once started.
        /// </summary>
        public TimeSpan ToolTimeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Display target (sanitized URL or command).
        /// </summary>
        public string Target { get; set; } = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>
        /// A shallow copy of this definition that connects to a different URL (the display <see cref="Target"/> is kept).
        /// </summary>
        /// <param name="url">The URL the copy connects to.</param>
        /// <returns>The copy.</returns>
        public RuntimeMcpServerDefinition WithUrl(string url)
        {
            RuntimeMcpServerDefinition copy = (RuntimeMcpServerDefinition)MemberwiseClone();
            copy.Url = url;
            return copy;
        }

        #endregion
    }
}
