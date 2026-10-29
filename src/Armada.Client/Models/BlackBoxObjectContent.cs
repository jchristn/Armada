namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// One read of a black box object (GET /api/v1/blackbox/{id}/objects/{name}): the bytes of the requested range, the
    /// object's content type, its ETag (the SHA-256 of the bytes returned), and where the next read starts.
    /// </summary>
    public class BlackBoxObjectContent
    {
        #region Public-Members

        /// <summary>
        /// Object name (for example evidence/git-status.txt).
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Content type of the object.
        /// </summary>
        public string ContentType { get; set; } = "application/octet-stream";

        /// <summary>
        /// ETag (the SHA-256 of <see cref="Content"/>, the bytes returned, without quotes), or null.
        /// </summary>
        public string? ETag { get; set; } = null;

        /// <summary>
        /// The stored object's SHA-256 (X-BlackBox-Stored-Sha256, over its stored, possibly gzip-encoded bytes), or null.
        /// </summary>
        public string? StoredSha256 { get; set; } = null;

        /// <summary>
        /// Byte offset of <see cref="Content"/> in the object.
        /// </summary>
        public long Offset { get; set; } = 0;

        /// <summary>
        /// Size of the whole object in bytes.
        /// </summary>
        public long TotalBytes { get; set; } = 0;

        /// <summary>
        /// Offset of the next read while more of the object remains, otherwise null.
        /// </summary>
        public long? NextOffset { get; set; } = null;

        /// <summary>
        /// The bytes read (at most 1 MB).
        /// </summary>
        public byte[] Content { get; set; } = Array.Empty<byte>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxObjectContent()
        {
        }

        #endregion
    }
}
