namespace Armada.Core.Harbor
{
    /// <summary>
    /// Server-to-Harbor reply to a heartbeat that carries a <see cref="HarborHeartbeat.Sequence"/>. The Admiral sends it
    /// as soon as it reads the heartbeat, before any other work, so the Harbor can time the link's round trip. Added to
    /// protocol 1.0 additively: the Admiral sends it only to a Harbor whose heartbeats carry a sequence.
    /// </summary>
    public class HarborHeartbeatAck : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// The <see cref="HarborHeartbeat.Sequence"/> of the heartbeat being acknowledged.
        /// </summary>
        public long Sequence { get; set; } = 0;

        #endregion
    }
}
