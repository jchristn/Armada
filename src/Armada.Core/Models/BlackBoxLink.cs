namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A mission related to a black box entry (blackbox_links).
    /// </summary>
    public class BlackBoxLink
    {
        /// <summary>
        /// Unique identifier (bbl_ prefix).
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or empty.</exception>
        public string Id
        {
            get => _Id;
            set => _Id = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(Id)) : value;
        }

        /// <summary>
        /// Entry.
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Tenant.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Related mission.
        /// </summary>
        public string MissionId { get; set; } = "";

        /// <summary>
        /// How it relates.
        /// </summary>
        public BlackBoxLinkRelationEnum Relation { get; set; } = BlackBoxLinkRelationEnum.OverlappingFiles;

        /// <summary>
        /// Commit, or null.
        /// </summary>
        public string? CommitHash { get; set; } = null;

        /// <summary>
        /// Overlapping files.
        /// </summary>
        public int OverlapFileCount { get; set; } = 0;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.BlackBoxLinkIdPrefix, 24);

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxLink()
        {
        }
    }
}
