namespace Armada.Runtimes.Mcp
{
    using System;

    /// <summary>
    /// Raised when an MCP request fails at the transport or protocol level (a non-success HTTP status or a
    /// JSON-RPC error envelope). Tool-call sites catch this and surface the message to the model as a failed
    /// tool result rather than aborting the whole agent loop.
    /// </summary>
    public class McpClientException : Exception
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">The error message.</param>
        public McpClientException(string message) : base(message)
        {
        }

        /// <summary>
        /// Instantiate with an inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public McpClientException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
