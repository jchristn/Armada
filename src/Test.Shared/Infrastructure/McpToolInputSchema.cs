namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The parts of an MCP tool input schema (JSON Schema object) that tests inspect: property names and types, and
    /// which properties are required.
    /// </summary>
    public sealed class McpToolInputSchema
    {
        #region Public-Members

        /// <summary>
        /// Properties by name.
        /// </summary>
        [JsonPropertyName("properties")]
        public Dictionary<string, McpToolInputProperty> Properties { get; set; } = new Dictionary<string, McpToolInputProperty>(StringComparer.Ordinal);

        /// <summary>
        /// Required property names.
        /// </summary>
        [JsonPropertyName("required")]
        public List<string> Required { get; set; } = new List<string>();

        #endregion
    }
}
