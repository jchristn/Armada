namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A prior attempt of the subject, or a link in its rescue chain.
    /// </summary>
    public class BlackBoxAttempt
    {
        /// <summary>
        /// Attempt number.
        /// </summary>
        public int Attempt { get; set; } = 0;

        /// <summary>
        /// Black box entry of the attempt, or null.
        /// </summary>
        public string? EntryId { get; set; } = null;

        /// <summary>
        /// Mission of the attempt (differs for a rescue).
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Stage outcome, or null.
        /// </summary>
        public string? StageOutcomeId { get; set; } = null;

        /// <summary>
        /// Failure kind, or null.
        /// </summary>
        public MissionFailureKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Reason excerpt (at most 500 characters).
        /// </summary>
        public string? ReasonExcerpt { get; set; } = null;

        /// <summary>
        /// Runtime name, or null.
        /// </summary>
        public string? Runtime { get; set; } = null;

        /// <summary>
        /// Model, or null.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Duration in milliseconds, or null.
        /// </summary>
        public long? DurationMs { get; set; } = null;

        /// <summary>
        /// When (UTC), or null.
        /// </summary>
        public DateTime? CreatedUtc { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxAttempt()
        {
        }
    }
}
