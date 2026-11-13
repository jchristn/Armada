namespace Armada.Server.Mcp
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>The <c>error.data</c> of a JSON-RPC invalid-params error for a missing required tool argument.</summary>
    public class McpMissingArgumentData
    {
        /// <summary>Name of the called tool.</summary>
        [JsonPropertyName("tool")]
        public string Tool { get; set; } = String.Empty;

        /// <summary>Name of the required argument the call omitted.</summary>
        [JsonPropertyName("argument")]
        public string Argument { get; set; } = String.Empty;
    }
}
