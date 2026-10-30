namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Related section: fingerprint, similar entries and cascade.
    /// </summary>
    public class BlackBoxRelated
    {
        /// <summary>
        /// Failure-mode fingerprint (BlackBoxFingerprint).
        /// </summary>
        public string? Fingerprint { get; set; } = null;

        /// <summary>
        /// Similar entries, newest first.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxSimilarEntries, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxSimilarEntry> SimilarEntries
        {
            get => _SimilarEntries;
            set => _SimilarEntries = value ?? new List<BlackBoxSimilarEntry>();
        }

        /// <summary>
        /// Entries with the same fingerprint in the last 30 days.
        /// </summary>
        public int OccurrencesLast30Days { get; set; } = 0;

        /// <summary>
        /// Armada versions the fingerprint was seen in (this entry's included), oldest sighting first.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxRelatedVersions, Reason = "Capped by BlackBoxPipeline")]
        public List<string> ArmadaVersions
        {
            get => _ArmadaVersions;
            set => _ArmadaVersions = value ?? new List<string>();
        }

        /// <summary>
        /// Cascade root entry, or null.
        /// </summary>
        public string? CascadeRootEntryId { get; set; } = null;

        /// <summary>
        /// Missions downstream of the cascade.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxDownstreamMissions, Reason = "Capped by BlackBoxRecordBounder")]
        public List<string> DownstreamMissionIds
        {
            get => _DownstreamMissionIds;
            set => _DownstreamMissionIds = value ?? new List<string>();
        }

        /// <summary>
        /// Successful landings on the same vessel within blackBox.nearbySuccessWindowHours of the failure that changed
        /// some of the same files (success flight recorder), most overlap first.
        /// </summary>
        [CollectionBound(BlackBoxInsightLimits.MaxNearbySuccesses, Reason = "Capped by BlackBoxNearbySuccessEnricher")]
        public List<BlackBoxNearbySuccess> NearbySuccesses
        {
            get => _NearbySuccesses;
            set => _NearbySuccesses = value ?? new List<BlackBoxNearbySuccess>();
        }

        /// <summary>
        /// Nearby successes found (NearbySuccesses holds the first ten).
        /// </summary>
        public int NearbySuccessTotal { get; set; } = 0;

        private List<BlackBoxNearbySuccess> _NearbySuccesses = new List<BlackBoxNearbySuccess>();
        private List<BlackBoxSimilarEntry> _SimilarEntries = new List<BlackBoxSimilarEntry>();
        private List<string> _DownstreamMissionIds = new List<string>();
        private List<string> _ArmadaVersions = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRelated()
        {
        }
    }
}
