namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// A JSON-RPC tools/call response with the tool result's isError flag.
    /// </summary>
    public class E2eMcpToolEnvelope
    {
        #region Public-Members

        /// <summary>
        /// Tool result, or null for a JSON-RPC error.
        /// </summary>
        public E2eMcpToolResult? Result { get; set; } = null;

        /// <summary>
        /// JSON-RPC error, or null.
        /// </summary>
        public McpRpcError? Error { get; set; } = null;

        #endregion
    }
}
