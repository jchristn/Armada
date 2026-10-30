namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Payload of the blackbox.deleted event.
    /// </summary>
    public class BlackBoxDeletedPayload
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
        /// Why: Manual, Purge, Expired, Evicted, Cascade.
        /// </summary>
        public string Reason { get; set; } = "Manual";

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxDeletedPayload()
        {
        }
    }
}
