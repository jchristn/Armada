namespace Test.Shared.Infrastructure.ApiSurface
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One server-pushed WebSocket event type.
    /// </summary>
    public sealed class ApiWebSocketEvent
    {
        #region Public-Members

        /// <summary>
        /// Event type.
        /// </summary>
        public string Type { get; set; } = "";

        /// <summary>
        /// Recipient scope (tenant, user, connection).
        /// </summary>
        public string Scope { get; set; } = "";

        /// <summary>
        /// Whether the envelope carries a message.
        /// </summary>
        public bool HasMessage { get; set; } = false;

        /// <summary>
        /// Entity type serialized as the payload, or null.
        /// </summary>
        public string? PayloadType { get; set; } = null;

        /// <summary>
        /// Payload field names.
        /// </summary>
        public List<string> Fields { get; set; } = new List<string>();

        /// <summary>
        /// Whether the event is a generic entity event.
        /// </summary>
        public bool Generic { get; set; } = false;

        #endregion
    }
}
