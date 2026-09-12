namespace Armada.Core.Harbor
{
    /// <summary>
    /// Server-to-Harbor reply to a handshake: whether the link was accepted and the MCP base URL launched
    /// captains should use to reach the Admiral.
    /// </summary>
    public class HarborHandshakeAck : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Whether the handshake was accepted. When false, <see cref="Reason"/> explains why and the link
        /// will be closed.
        /// </summary>
        public bool Accepted { get; set; } = false;

        /// <summary>
        /// MCP base URL launched captains should use to reach the Admiral's MCP server, or null when not
        /// applicable.
        /// </summary>
        public string? McpBaseUrl { get; set; } = null;

        /// <summary>
        /// Human-readable reason the handshake was rejected, or null on acceptance.
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion
    }
}
