namespace Armada.Server.Mcp
{
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The <c>params</c> object of an MCP <c>tools/call</c> request, read only to decide whether a declared
    /// required argument is absent before the call is handed to Voltaic's own dispatcher.
    /// </summary>
    public class McpToolCallEnvelope
    {
        /// <summary>Name of the tool being called.</summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>Tool arguments keyed by property name; null when the request sends no arguments object.</summary>
        [JsonPropertyName("arguments")]
        public Dictionary<string, JsonElement>? Arguments { get; set; } = null;
    }
}
