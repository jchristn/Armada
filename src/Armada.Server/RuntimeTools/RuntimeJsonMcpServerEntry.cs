namespace Armada.Server.RuntimeTools
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One entry of the <c>mcpServers</c> map in a Claude Code (<c>~/.claude.json</c>), Gemini CLI
    /// (<c>~/.gemini/settings.json</c>), or Cursor (<c>.cursor/mcp.json</c>) config file. Covers the members each runtime
    /// uses to describe a server; unknown members are ignored.
    /// </summary>
    public class RuntimeJsonMcpServerEntry
    {
        #region Public-Members

        /// <summary>
        /// Transport type (Claude Code: "stdio", "http", "sse").
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Transport type under the alternative member name some runtimes use.
        /// </summary>
        [JsonPropertyName("transport")]
        public string? Transport { get; set; } = null;

        /// <summary>
        /// Server URL for HTTP transports.
        /// </summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; } = null;

        /// <summary>
        /// Streamable HTTP URL (Gemini CLI).
        /// </summary>
        [JsonPropertyName("httpUrl")]
        public string? HttpUrl { get; set; } = null;

        /// <summary>
        /// Command for the stdio transport.
        /// </summary>
        [JsonPropertyName("command")]
        public string? Command { get; set; } = null;

        /// <summary>
        /// Command arguments for the stdio transport.
        /// </summary>
        [JsonPropertyName("args")]
        public List<string>? Args { get; set; } = null;

        /// <summary>
        /// Environment variables (string values only).
        /// </summary>
        [JsonPropertyName("env")]
        [JsonConverter(typeof(StringValuesDictionaryConverter))]
        public Dictionary<string, string>? Env { get; set; } = null;

        /// <summary>
        /// HTTP headers (string values only).
        /// </summary>
        [JsonPropertyName("headers")]
        [JsonConverter(typeof(StringValuesDictionaryConverter))]
        public Dictionary<string, string>? Headers { get; set; } = null;

        #endregion
    }
}
