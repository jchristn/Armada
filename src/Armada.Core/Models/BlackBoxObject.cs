namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One object (file) of a black box entry (blackbox_objects). Data holds the bytes only for the Database target and only when a read asks for them; it is never serialized.
    /// </summary>
    public class BlackBoxObject
    {
        /// <summary>
        /// Unique identifier (bbo_ prefix).
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or empty.</exception>
        public string Id
        {
            get => _Id;
            set => _Id = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(Id)) : value;
        }

        /// <summary>
        /// Entry (bbx_ prefix).
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Tenant.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Object name, unique per entry: record.json, brief.md, evidence/... (entries written before v1.2.0 shipped may also hold brief.user.md and brief.maintainer.md)
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; set; } = "application/octet-stream";

        /// <summary>
        /// gzip or identity.
        /// </summary>
        public string ContentEncoding { get; set; } = "identity";

        /// <summary>
        /// Stored (encoded) size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Uncompressed size in bytes.
        /// </summary>
        public long UncompressedBytes { get; set; } = 0;

        /// <summary>
        /// SHA-256 of the stored bytes (lowercase hex), checked on every whole read.
        /// </summary>
        public string? Sha256 { get; set; } = null;

        /// <summary>
        /// Full key for external targets; null for Database.
        /// </summary>
        public string? StorageKey { get; set; } = null;

        /// <summary>
        /// Whether the bytes are missing from the store.
        /// </summary>
        public bool IsMissing { get; set; } = false;

        /// <summary>
        /// Stored bytes (Database target only; populated by reads that ask for data).
        /// </summary>
        [JsonIgnore]
        public byte[]? Data { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.BlackBoxObjectIdPrefix, 24);

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxObject()
        {
        }
    }
}
