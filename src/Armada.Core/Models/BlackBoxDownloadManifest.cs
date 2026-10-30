namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// manifest.json inside a black box .zip download: the entry, its schema version and every object with name, content type, size and SHA-256.
    /// </summary>
    public class BlackBoxDownloadManifest
    {
        /// <summary>
        /// Entry.
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Record schema version.
        /// </summary>
        public int SchemaVersion { get; set; } = BlackBoxLimits.SchemaVersion;

        /// <summary>
        /// Mission.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Voyage.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Armada version that captured the entry.
        /// </summary>
        public string? ArmadaVersion { get; set; } = null;

        /// <summary>
        /// Entry creation (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the download was built (UTC).
        /// </summary>
        public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Objects in the archive (stored SHA-256 of the encoded bytes; the archive holds the decoded bytes).
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxObjectsPerEntry, Reason = "One per object of the entry")]
        public List<BlackBoxObjectSummary> Objects
        {
            get => _Objects;
            set => _Objects = value ?? new List<BlackBoxObjectSummary>();
        }

        /// <summary>
        /// Objects whose bytes could not be read.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxObjectsPerEntry, Reason = "One per object of the entry")]
        public List<string> MissingObjects
        {
            get => _MissingObjects;
            set => _MissingObjects = value ?? new List<string>();
        }

        private List<BlackBoxObjectSummary> _Objects = new List<BlackBoxObjectSummary>();
        private List<string> _MissingObjects = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxDownloadManifest()
        {
        }
    }
}
