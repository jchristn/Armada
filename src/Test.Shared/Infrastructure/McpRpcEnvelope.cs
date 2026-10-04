namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// Typed JSON-RPC response envelope returned by the MCP endpoint for a tools/call request.
    /// </summary>
    public sealed class McpRpcEnvelope
    {
        /// <summary>
        /// The tool call result, or null when the server returned an error.
        /// </summary>
        public McpToolCallResult? Result { get; set; } = null;

        /// <summary>
        /// The JSON-RPC error, or null on success.
        /// </summary>
        public McpRpcError? Error { get; set; } = null;
    }
}
