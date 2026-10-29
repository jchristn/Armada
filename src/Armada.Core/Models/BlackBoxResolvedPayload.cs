namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Payload of the blackbox.resolved event.
    /// </summary>
    public class BlackBoxResolvedPayload
    {
        /// <summary>
        /// Entry.
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Mission.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Voyage.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Resolution.
        /// </summary>
        public BlackBoxResolutionEnum Resolution { get; set; } = BlackBoxResolutionEnum.Resolved;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxResolvedPayload()
        {
        }
    }
}
