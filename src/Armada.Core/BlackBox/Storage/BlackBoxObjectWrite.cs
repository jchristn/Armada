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
    /// Bytes to write for one object. Data is already encoded (gzip or identity) by BlackBoxObjectCodec.
    /// </summary>
    public class BlackBoxObjectWrite
    {
        /// <summary>
        /// Where to write.
        /// </summary>
        public BlackBoxObjectRef Reference { get; set; } = new BlackBoxObjectRef();

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; set; } = "application/octet-stream";

        /// <summary>
        /// gzip or identity.
        /// </summary>
        public string ContentEncoding { get; set; } = "identity";

        /// <summary>
        /// Encoded bytes.
        /// </summary>
        public byte[] Data { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// SHA-256 of Data (lowercase hex).
        /// </summary>
        public string? Sha256 { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxObjectWrite()
        {
        }
    }
}
