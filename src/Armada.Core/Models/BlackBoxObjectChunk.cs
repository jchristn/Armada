namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A chunk of a black box object, for paging through large objects (MCP read_blackbox_object: at most 64 KB per call).
    /// </summary>
    public class BlackBoxObjectChunk
    {
        /// <summary>
        /// Entry.
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Object name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; set; } = "application/octet-stream";

        /// <summary>
        /// Offset of this chunk in the uncompressed object.
        /// </summary>
        public long Offset { get; set; } = 0;

        /// <summary>
        /// Bytes in this chunk.
        /// </summary>
        public long Length { get; set; } = 0;

        /// <summary>
        /// Uncompressed size of the object.
        /// </summary>
        public long TotalBytes { get; set; } = 0;

        /// <summary>
        /// Offset of the next chunk, or null at the end.
        /// </summary>
        public long? NextOffset { get; set; } = null;

        /// <summary>
        /// utf-8 for text objects, base64 otherwise.
        /// </summary>
        public string Encoding { get; set; } = "utf-8";

        /// <summary>
        /// Chunk content (text, or base64).
        /// </summary>
        public string Content { get; set; } = "";

        /// <summary>
        /// SHA-256 of the stored object.
        /// </summary>
        public string? Sha256 { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxObjectChunk()
        {
        }
    }
}
