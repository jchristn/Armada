namespace Test.Shared.Infrastructure.ApiSurface
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The WebSocket contract in the API surface.
    /// </summary>
    public sealed class ApiWebSocketSurface
    {
        #region Public-Members

        /// <summary>
        /// WebSocket endpoints.
        /// </summary>
        public List<ApiWebSocketEndpoint> Endpoints { get; set; } = new List<ApiWebSocketEndpoint>();

        /// <summary>
        /// Client-to-server routes.
        /// </summary>
        public List<string> Routes { get; set; } = new List<string>();

        /// <summary>
        /// Fields a client message may carry.
        /// </summary>
        public List<string> ClientMessageFields { get; set; } = new List<string>();

        /// <summary>
        /// Envelope fields of server-pushed events.
        /// </summary>
        public List<string> EventEnvelopeFields { get; set; } = new List<string>();

        /// <summary>
        /// Envelope fields of command replies.
        /// </summary>
        public List<string> CommandReplyFields { get; set; } = new List<string>();

        /// <summary>
        /// Command actions.
        /// </summary>
        public List<string> Commands { get; set; } = new List<string>();

        /// <summary>
        /// Server-pushed event types.
        /// </summary>
        public List<ApiWebSocketEvent> Events { get; set; } = new List<ApiWebSocketEvent>();

        #endregion
    }
}
