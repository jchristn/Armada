namespace Test.Shared.Infrastructure
{
    using System.Text.Json.Serialization;
    using Armada.Core.Protocol;

    /// <summary>
    /// The params of a request received by <see cref="ToolClientStubHandler"/>.
    /// </summary>
    public sealed class ToolClientStubParams
    {
        #region Public-Members

        /// <summary>
        /// tools/list cursor, or null.
        /// </summary>
        [JsonPropertyName("cursor")]
        public string? Cursor { get; set; } = null;

        /// <summary>
        /// tools/call tool name, or null.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// tools/call arguments as raw JSON, or null.
        /// </summary>
        [JsonPropertyName("arguments")]
        [JsonConverter(typeof(RawJsonStringConverter))]
        public string? Arguments { get; set; } = null;

        #endregion
    }
}
