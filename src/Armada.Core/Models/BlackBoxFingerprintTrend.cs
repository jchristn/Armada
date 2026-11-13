namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Occurrences of one failure mode (fingerprint) per Armada version, oldest version first by first sighting, to see
    /// whether a release fixed it (a version with no entries after a fix shipped).
    /// </summary>
    public class BlackBoxFingerprintTrend
    {
        /// <summary>
        /// Fingerprint.
        /// </summary>
        public string Fingerprint { get; set; } = "";

        /// <summary>
        /// Phase of the newest entry.
        /// </summary>
        public BlackBoxPhaseEnum Phase { get; set; } = BlackBoxPhaseEnum.Unknown;

        /// <summary>
        /// Step of the newest entry.
        /// </summary>
        public BlackBoxStepEnum Step { get; set; } = BlackBoxStepEnum.Unknown;

        /// <summary>
        /// Entries counted (at most <see cref="BlackBoxLimits.MaxTrendEntries"/>).
        /// </summary>
        public long Total { get; set; } = 0;

        /// <summary>
        /// True when the fingerprint has more entries than were counted.
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Counts per Armada version, ordered by first sighting.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxTrendVersions, Reason = "Capped by BlackBoxService.GetFingerprintTrendAsync")]
        public List<BlackBoxVersionCount> Versions
        {
            get => _Versions;
            set => _Versions = value ?? new List<BlackBoxVersionCount>();
        }

        private List<BlackBoxVersionCount> _Versions = new List<BlackBoxVersionCount>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxFingerprintTrend()
        {
        }
    }
}
