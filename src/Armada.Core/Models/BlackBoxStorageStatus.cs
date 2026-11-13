namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Black box storage status: configured target, health and usage by target (Admin).
    /// </summary>
    public class BlackBoxStorageStatus
    {
        /// <summary>
        /// Configured target for new entries.
        /// </summary>
        public BlackBoxStorageTargetEnum Target { get; set; } = BlackBoxStorageTargetEnum.Database;

        /// <summary>
        /// Health of the configured target.
        /// </summary>
        public BlackBoxStoreHealth Health { get; set; } = new BlackBoxStoreHealth();

        /// <summary>
        /// Whether failed external writes fall back to the database.
        /// </summary>
        public bool FallbackToDatabase { get; set; } = false;

        /// <summary>
        /// Usage by target.
        /// </summary>
        [CollectionBound(7, Reason = "One per BlackBoxStorageTargetEnum value")]
        public List<BlackBoxStorageTargetUsage> Usage
        {
            get => _Usage;
            set => _Usage = value ?? new List<BlackBoxStorageTargetUsage>();
        }

        /// <summary>
        /// Running storage migration (bbm_ prefix), or null.
        /// </summary>
        public string? ActiveMigrationId { get; set; } = null;

        private List<BlackBoxStorageTargetUsage> _Usage = new List<BlackBoxStorageTargetUsage>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxStorageStatus()
        {
        }
    }
}
