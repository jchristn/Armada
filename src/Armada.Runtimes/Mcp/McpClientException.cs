namespace Armada.Runtimes.Mcp
{
    using System;

    /// <summary>
    /// Raised when an MCP request fails at the transport or protocol level (a non-success HTTP status, a JSON-RPC
    /// error envelope, or a body with no response for the request). Tool-call sites catch this and surface the message
    /// to the model as a failed tool result rather than aborting the whole agent loop. Callers that must distinguish
    /// causes read <see cref="HttpStatusCode"/> and <see cref="JsonRpcCode"/>, never the message.
    /// </summary>
    public class McpClientException : Exception
    {
        #region Public-Members

        /// <summary>
        /// HTTP status code of a non-success response, or null when the failure was not an HTTP status.
        /// </summary>
        public int? HttpStatusCode { get; } = null;

        /// <summary>
        /// JSON-RPC error code from the server's error envelope, or null when the failure was not a JSON-RPC error.
        /// </summary>
        public int? JsonRpcCode { get; } = null;

        #endregion

        #region Constructors-and-Factories

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

        /// <summary>
        /// Instantiate with the HTTP status and JSON-RPC code that describe the failure.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="httpStatusCode">HTTP status code, or null.</param>
        /// <param name="jsonRpcCode">JSON-RPC error code, or null.</param>
        public McpClientException(string message, int? httpStatusCode, int? jsonRpcCode) : base(message)
        {
            HttpStatusCode = httpStatusCode;
            JsonRpcCode = jsonRpcCode;
        }

        #endregion
    }
}
