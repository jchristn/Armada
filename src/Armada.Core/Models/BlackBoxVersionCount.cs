namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// How often a failure mode (fingerprint) occurred under one Armada version.
    /// </summary>
    public class BlackBoxVersionCount
    {
        /// <summary>
        /// Armada version ("unknown" when the entry did not record one).
        /// </summary>
        public string ArmadaVersion { get; set; } = "unknown";

        /// <summary>
        /// Entries with this fingerprint under this version.
        /// </summary>
        public long Count { get; set; } = 0;

        /// <summary>
        /// Of those, entries still Open.
        /// </summary>
        public long OpenCount { get; set; } = 0;

        /// <summary>
        /// Of those, entries resolved as ArmadaFixed.
        /// </summary>
        public long ArmadaFixedCount { get; set; } = 0;

        /// <summary>
        /// Earliest entry under this version.
        /// </summary>
        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Latest entry under this version.
        /// </summary>
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxVersionCount()
        {
        }
    }
}
