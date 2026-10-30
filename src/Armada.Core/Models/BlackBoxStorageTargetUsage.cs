namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Black box usage of one storage target.
    /// </summary>
    public class BlackBoxStorageTargetUsage
    {
        /// <summary>
        /// Target.
        /// </summary>
        public BlackBoxStorageTargetEnum Target { get; set; } = BlackBoxStorageTargetEnum.Database;

        /// <summary>
        /// Entries stored there.
        /// </summary>
        public long Entries { get; set; } = 0;

        /// <summary>
        /// Stored bytes.
        /// </summary>
        public long Bytes { get; set; } = 0;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxStorageTargetUsage()
        {
        }
    }
}
