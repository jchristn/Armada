namespace Armada.Core.Models
{
    /// <summary>
    /// Request to open a local Armada dashboard websocket session on behalf of a proxy browser client.
    /// </summary>
    public class RemoteTunnelWebSocketOpenRequest
    {
        #region Public-Members

        /// <summary>
        /// Stable proxy-side browser websocket identifier.
        /// </summary>
        public string ProxySocketId { get; set; } = String.Empty;

        /// <summary>
        /// Requested websocket path.
        /// </summary>
        public string Path { get; set; } = "/ws";

        /// <summary>
        /// Raw query string of the browser's upgrade request (without the leading '?'), forwarded so the local /ws
        /// upgrade carries the browser's <c>token</c> parameter. Null when the browser sent none.
        /// </summary>
        public string? QueryString { get; set; } = null;

        /// <summary>
        /// Raw Sec-WebSocket-Protocol header of the browser's upgrade request, forwarded as subprotocols of the local
        /// /ws upgrade so a protocol-carried token still authenticates. Null when the browser sent none.
        /// </summary>
        public string? Subprotocols { get; set; } = null;

        #endregion
    }
}
