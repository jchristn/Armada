namespace Armada.Core.Harbor
{
    using System.Collections.Generic;

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

        /// <summary>
        /// The jobs the Admiral still owns on this Harbor (it is waiting on their lifecycle), or null from an Admiral that
        /// predates the field. A Harbor stops every job it was running when it opened the link that is not in the list:
        /// the Admiral gave up on it while the link was down (for example the mission was recovered elsewhere while the
        /// host slept), so a second agent does not work in the same dock. A Harbor that predates the field ignores it.
        /// </summary>
        public List<string>? OwnedJobIds { get; set; } = null;

        /// <summary>
        /// The highest <see cref="HarborMessage.OutboxSequence"/> the Admiral has received from this Harbor's job outbox
        /// (the epoch in the handshake), or null from an Admiral that does not acknowledge job messages. The Harbor
        /// drops what was received and replays the rest; with null it replays only what it never sent.
        /// </summary>
        public long? LastJobSequence { get; set; } = null;

        /// <summary>
        /// Capability flags the Admiral expects that this Harbor did not advertise (see
        /// <see cref="HarborCapabilityFlags.Advertised"/>), or null when none are missing or the Admiral predates the field.
        /// The Admiral falls back to the earlier behavior for each one; the Harbor shows "Harbor update recommended".
        /// </summary>
        public List<string>? MissingCapabilities { get; set; } = null;

        #endregion
    }
}
