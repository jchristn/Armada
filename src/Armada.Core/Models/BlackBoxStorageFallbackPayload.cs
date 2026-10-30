namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Payload of the blackbox.storage_fallback event.
    /// </summary>
    public class BlackBoxStorageFallbackPayload
    {
        /// <summary>
        /// Entry.
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Target that failed.
        /// </summary>
        public BlackBoxStorageTargetEnum From { get; set; } = BlackBoxStorageTargetEnum.Database;

        /// <summary>
        /// Target written instead.
        /// </summary>
        public BlackBoxStorageTargetEnum To { get; set; } = BlackBoxStorageTargetEnum.Database;

        /// <summary>
        /// Error (no secrets).
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxStorageFallbackPayload()
        {
        }
    }
}
