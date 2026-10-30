namespace Armada.Core.BlackBox.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Bytes read from a black box object: the whole object or a range of it (uncompressed).
    /// </summary>
    public class BlackBoxObjectReadResult
    {
        /// <summary>
        /// Object metadata (Data not populated).
        /// </summary>
        public BlackBoxObject Object { get; set; } = new BlackBoxObject();

        /// <summary>
        /// The bytes read (uncompressed).
        /// </summary>
        public byte[] Data { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Offset of Data in the uncompressed object.
        /// </summary>
        public long Offset { get; set; } = 0;

        /// <summary>
        /// Uncompressed size of the object.
        /// </summary>
        public long TotalBytes { get; set; } = 0;

        /// <summary>
        /// Whether the SHA-256 was checked for this read (whole reads and gzip objects).
        /// </summary>
        public bool Verified { get; set; } = false;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxObjectReadResult()
        {
        }
    }
}
