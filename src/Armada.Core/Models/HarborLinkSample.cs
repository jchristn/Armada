namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Link health of one Harbor over one minute: how many heartbeats arrived, the round-trip times they reported, and
    /// the Harbor's reconnect counters as of the last of them. The Admiral accumulates heartbeats in memory and writes one
    /// row per Harbor per minute.
    /// </summary>
    public class HarborLinkSample
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (hls_ prefix).
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Harbor identifier.
        /// </summary>
        public string HarborId { get; set; } = String.Empty;

        /// <summary>
        /// Start of the minute the sample covers (UTC).
        /// </summary>
        public DateTime BucketStartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Heartbeats received in the minute.
        /// </summary>
        public int HeartbeatCount { get; set; } = 0;

        /// <summary>
        /// Heartbeats in the minute that reported a round-trip time.
        /// </summary>
        public int RoundTripCount { get; set; } = 0;

        /// <summary>
        /// Sum of the reported round-trip times, in milliseconds.
        /// </summary>
        public long RoundTripTotalMs { get; set; } = 0;

        /// <summary>
        /// Largest reported round-trip time, in milliseconds, or null when none was reported.
        /// </summary>
        public long? RoundTripMaxMs { get; set; } = null;

        /// <summary>
        /// The Harbor's reconnect count as of its last heartbeat in the minute, or null from a Harbor that does not report it.
        /// </summary>
        public int? ReconnectCount { get; set; } = null;

        /// <summary>
        /// When the Harbor last reconnected (UTC, its clock), as of its last heartbeat in the minute, or null.
        /// </summary>
        public DateTime? LastReconnectUtc { get; set; } = null;

        /// <summary>
        /// Creation timestamp (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.HarborLinkSampleIdPrefix, 24);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborLinkSample()
        {
        }

        #endregion
    }
}
