namespace Armada.Runtimes
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Kind of a diagnostic emitted by the in-process (ApiEndpoint) runtime alongside the model's reply text.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ApiRuntimeDiagnosticKindEnum
    {
        /// <summary>
        /// MCP connection status (connected, or unavailable).
        /// </summary>
        McpStatus,

        /// <summary>
        /// A tool call is starting.
        /// </summary>
        ToolCall,

        /// <summary>
        /// A tool call finished.
        /// </summary>
        ToolResult,

        /// <summary>
        /// A non-fatal warning (for example the iteration limit was reached).
        /// </summary>
        Warning,

        /// <summary>
        /// A fatal error (the inference call or the loop failed).
        /// </summary>
        Error,

        /// <summary>
        /// The run was cancelled.
        /// </summary>
        Cancelled
    }
}
