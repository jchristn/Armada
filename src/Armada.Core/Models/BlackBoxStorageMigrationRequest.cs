namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Start a black box storage migration (Admin).
    /// </summary>
    public class BlackBoxStorageMigrationRequest
    {
        /// <summary>
        /// Target to move entries to.
        /// </summary>
        public BlackBoxStorageTargetEnum To { get; set; } = BlackBoxStorageTargetEnum.Database;

        /// <summary>
        /// Only move entries currently on this target; null moves every other target.
        /// </summary>
        public BlackBoxStorageTargetEnum? From { get; set; } = null;

        /// <summary>
        /// Delete the source copy after the copy is verified.
        /// </summary>
        public bool DeleteSource { get; set; } = true;

        /// <summary>
        /// Entries per batch (1 to 1000).
        /// </summary>
        public int BatchSize { get; set; } = 50;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxStorageMigrationRequest()
        {
        }
    }
}
