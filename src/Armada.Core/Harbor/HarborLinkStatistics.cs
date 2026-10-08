namespace Armada.Core.Harbor
{
    using System;

    /// <summary>
    /// Link counters a Harbor keeps across its link sessions: how many times the Admiral accepted its handshake, and when
    /// it last reconnected. A Harbor runs one <see cref="Armada.Core.Services.HarborLinkClient"/> per session, so the
    /// process that owns the reconnect loop creates one instance and gives it to every session's client. Thread-safe.
    /// </summary>
    public class HarborLinkStatistics
    {
        #region Public-Members

        /// <summary>
        /// Link sessions the Admiral accepted in this process.
        /// </summary>
        public int AcceptedSessions
        {
            get { lock (_Lock) return _AcceptedSessions; }
        }

        /// <summary>
        /// Times the link was re-established after the first accepted session.
        /// </summary>
        public int ReconnectCount
        {
            get { lock (_Lock) return _AcceptedSessions > 1 ? _AcceptedSessions - 1 : 0; }
        }

        /// <summary>
        /// When the link was last re-established (UTC), or null when it has not reconnected.
        /// </summary>
        public DateTime? LastReconnectUtc
        {
            get { lock (_Lock) return _LastReconnectUtc; }
        }

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private int _AcceptedSessions = 0;
        private DateTime? _LastReconnectUtc = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborLinkStatistics()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record that the Admiral accepted a session's handshake. Every acceptance after the first is a reconnect.
        /// </summary>
        /// <param name="acceptedUtc">When it was accepted (UTC).</param>
        public void RecordAccepted(DateTime acceptedUtc)
        {
            lock (_Lock)
            {
                _AcceptedSessions++;
                if (_AcceptedSessions > 1) _LastReconnectUtc = DateTime.SpecifyKind(acceptedUtc.ToUniversalTime(), DateTimeKind.Utc);
            }
        }

        #endregion
    }
}
