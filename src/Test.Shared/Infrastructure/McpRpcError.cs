namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// Typed JSON-RPC error object.
    /// </summary>
    public sealed class McpRpcError
    {
        /// <summary>
        /// Error code.
        /// </summary>
        public int Code { get; set; } = 0;

        /// <summary>
        /// Error message.
        /// </summary>
        public string? Message { get; set; } = null;
    }
}
