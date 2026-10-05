namespace Armada.Core.Models
{
    /// <summary>
    /// Machine-readable codes the Admiral sets in <see cref="RemoteTunnelStatus.LastErrorCode"/> for errors it detects
    /// itself. Errors the proxy reports carry the proxy's own envelope ErrorCode (for example invalid_handshake).
    /// </summary>
    public static class RemoteTunnelErrorCodes
    {
        /// <summary>
        /// Remote control is enabled but no tunnel URL is configured.
        /// </summary>
        public const string MissingTunnelUrl = "missing_tunnel_url";

        /// <summary>
        /// The tunnel URL is not an absolute URI.
        /// </summary>
        public const string InvalidTunnelUrl = "invalid_tunnel_url";

        /// <summary>
        /// The tunnel URL scheme is not ws, wss, http, or https.
        /// </summary>
        public const string UnsupportedScheme = "unsupported_scheme";

        /// <summary>
        /// Connecting to the proxy timed out.
        /// </summary>
        public const string ConnectTimeout = "connect_timeout";

        /// <summary>
        /// The WebSocket connection failed.
        /// </summary>
        public const string WebSocketError = "websocket_error";

        /// <summary>
        /// Any other tunnel failure.
        /// </summary>
        public const string TunnelFailure = "tunnel_failure";

        /// <summary>
        /// The proxy rejected a request or the handshake without an error code of its own.
        /// </summary>
        public const string Rejected = "rejected";
    }
}
