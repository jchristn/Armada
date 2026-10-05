namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where a captain tool or tool server in the runtime tool catalog comes from.
    /// Serialized by name (wire values unchanged from the earlier string field).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CaptainToolSourceKindEnum
    {
        /// <summary>
        /// Internal runtime support that is neither an MCP server nor a runtime built-in tool.
        /// </summary>
        Internal,

        /// <summary>
        /// An MCP server configured for the runtime, or a tool exposed by one.
        /// </summary>
        McpServer,

        /// <summary>
        /// A tool built into the runtime itself, or the runtime as a tool source.
        /// </summary>
        RuntimeBuiltIn
    }
}
