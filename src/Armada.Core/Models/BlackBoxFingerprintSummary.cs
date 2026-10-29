namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One failure mode: entries sharing a fingerprint.
    /// </summary>
    public class BlackBoxFingerprintSummary
    {
        /// <summary>
        /// Fingerprint.
        /// </summary>
        public string Fingerprint { get; set; } = "";

        /// <summary>
        /// Entries.
        /// </summary>
        public long Count { get; set; } = 0;

        /// <summary>
        /// Entries still Open.
        /// </summary>
        public long OpenCount { get; set; } = 0;

        /// <summary>
        /// First seen (UTC).
        /// </summary>
        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last seen (UTC).
        /// </summary>
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Newest entry.
        /// </summary>
        public string? LatestEntryId { get; set; } = null;

        /// <summary>
        /// Phase of the newest entry.
        /// </summary>
        public BlackBoxPhaseEnum Phase { get; set; } = BlackBoxPhaseEnum.Unknown;

        /// <summary>
        /// Step of the newest entry.
        /// </summary>
        public BlackBoxStepEnum Step { get; set; } = BlackBoxStepEnum.Unknown;

        /// <summary>
        /// Failure kind of the newest entry.
        /// </summary>
        public MissionFailureKindEnum? FailureKind { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxFingerprintSummary()
        {
        }
    }
}
