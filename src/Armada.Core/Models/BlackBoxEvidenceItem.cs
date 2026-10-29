namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One evidence object listed in a record.
    /// </summary>
    public class BlackBoxEvidenceItem
    {
        /// <summary>
        /// Object name (for example evidence/mission.log).
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Content type.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// Stored size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Uncompressed size in bytes.
        /// </summary>
        public long UncompressedBytes { get; set; } = 0;

        /// <summary>
        /// Short excerpt (at most 500 characters).
        /// </summary>
        public string? Excerpt { get; set; } = null;

        /// <summary>
        /// Whether the object was cut (head and tail kept).
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Original size before cutting, or null.
        /// </summary>
        public long? OriginalBytes { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxEvidenceItem()
        {
        }
    }
}
