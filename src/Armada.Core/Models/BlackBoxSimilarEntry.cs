namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// An earlier entry with the same failure mode.
    /// </summary>
    public class BlackBoxSimilarEntry
    {
        /// <summary>
        /// Entry.
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Mission.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Mission title.
        /// </summary>
        public string? MissionTitle { get; set; } = null;

        /// <summary>
        /// When (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// How it was resolved.
        /// </summary>
        public BlackBoxResolutionEnum Resolution { get; set; } = BlackBoxResolutionEnum.Open;

        /// <summary>
        /// Resolution note excerpt.
        /// </summary>
        public string? ResolutionNote { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxSimilarEntry()
        {
        }
    }
}
