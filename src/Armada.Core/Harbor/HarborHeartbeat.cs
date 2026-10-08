namespace Armada.Core.Harbor
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Harbor-to-server periodic liveness message. Also carries the set of jobs the Harbor still has
    /// running, so the Admiral can rebind them after a reconnect and reconcile any it thought were lost, and the
    /// Harbor's view of its link's health (round-trip time and reconnects). The link-health fields were added to
    /// protocol 1.0 additively: a Harbor that predates them omits them, and an Admiral that predates them ignores them.
    /// </summary>
    public class HarborHeartbeat : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Job identifiers currently alive on the Harbor.
        /// </summary>
        public List<string> LiveJobIds { get; set; } = new List<string>();

        /// <summary>
        /// Heartbeat number within this link session, starting at 1. When present, the Admiral answers with a
        /// <see cref="HarborHeartbeatAck"/> echoing it, which the Harbor times to measure the link's round trip. Null from
        /// a Harbor that does not measure round trips (the Admiral then sends no acknowledgement).
        /// </summary>
        public long? Sequence { get; set; } = null;

        /// <summary>
        /// Round-trip time of the most recent heartbeat the Admiral acknowledged on this link, in milliseconds: from the
        /// Harbor writing that heartbeat to the socket to it reading the matching acknowledgement, on the Harbor's
        /// monotonic clock. Null on the first heartbeat of a session, or when no acknowledgement has arrived.
        /// </summary>
        public long? LastRoundTripMs { get; set; } = null;

        /// <summary>
        /// How many times this Harbor process has re-established its link after its first connection. Null from a Harbor
        /// that does not report it.
        /// </summary>
        public int? ReconnectCount { get; set; } = null;

        /// <summary>
        /// When this Harbor process last re-established its link (UTC, on the Harbor's clock), or null when it has not
        /// reconnected.
        /// </summary>
        public DateTime? LastReconnectUtc { get; set; } = null;

        #endregion
    }
}
