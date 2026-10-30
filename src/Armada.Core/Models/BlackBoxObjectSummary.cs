namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Short form of a black box object for lists.
    /// </summary>
    public class BlackBoxObjectSummary
    {
        /// <summary>
        /// Object name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; set; } = "application/octet-stream";

        /// <summary>
        /// Stored encoding.
        /// </summary>
        public string ContentEncoding { get; set; } = "identity";

        /// <summary>
        /// Stored size.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Uncompressed size.
        /// </summary>
        public long UncompressedBytes { get; set; } = 0;

        /// <summary>
        /// SHA-256 of the stored bytes.
        /// </summary>
        public string? Sha256 { get; set; } = null;

        /// <summary>
        /// Whether the bytes are missing.
        /// </summary>
        public bool IsMissing { get; set; } = false;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxObjectSummary()
        {
        }
    }
}
