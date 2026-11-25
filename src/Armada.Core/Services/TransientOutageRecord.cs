namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// When a mission's transient model outage started; the outage grace is measured from it.
    /// </summary>
    public class TransientOutageRecord
    {
        /// <summary>
        /// Start of the outage (UTC).
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    }
}
