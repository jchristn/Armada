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
    /// One key found in a store by EnumerateAsync.
    /// </summary>
    public class BlackBoxStoredKey
    {
        /// <summary>
        /// Key (for Database: {entryId}/{objectName}).
        /// </summary>
        public string Key { get; set; } = "";

        /// <summary>
        /// Stored size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Last modified (UTC), or null when unknown.
        /// </summary>
        public DateTime? LastModifiedUtc { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxStoredKey()
        {
        }
    }
}
