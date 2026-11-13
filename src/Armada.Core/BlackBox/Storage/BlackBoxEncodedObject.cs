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
    /// An object after encoding for storage.
    /// </summary>
    public class BlackBoxEncodedObject
    {
        /// <summary>
        /// Stored bytes.
        /// </summary>
        public byte[] Data { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// gzip or identity.
        /// </summary>
        public string ContentEncoding { get; set; } = "identity";

        /// <summary>
        /// SHA-256 of Data (lowercase hex).
        /// </summary>
        public string Sha256 { get; set; } = "";

        /// <summary>
        /// Size before encoding.
        /// </summary>
        public long UncompressedBytes { get; set; } = 0;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxEncodedObject()
        {
        }
    }
}
