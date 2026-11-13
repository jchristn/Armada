namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Payload of the blackbox.analyzed event.
    /// </summary>
    public class BlackBoxAnalyzedPayload
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
        /// State after the pipeline.
        /// </summary>
        public BlackBoxStateEnum State { get; set; } = BlackBoxStateEnum.Analyzed;

        /// <summary>
        /// Triage.
        /// </summary>
        public BlackBoxTriageEnum Triage { get; set; } = BlackBoxTriageEnum.Unknown;

        /// <summary>
        /// Rules version.
        /// </summary>
        public int RulesVersion { get; set; } = 0;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxAnalyzedPayload()
        {
        }
    }
}
