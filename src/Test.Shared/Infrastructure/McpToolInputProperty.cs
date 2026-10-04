namespace Test.Shared.Infrastructure
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One property of an MCP tool input schema.
    /// </summary>
    public sealed class McpToolInputProperty
    {
        #region Public-Members

        /// <summary>
        /// JSON Schema type (string, integer, number, boolean, array, object).
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        #endregion
    }
}
