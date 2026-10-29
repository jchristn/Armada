namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A configuration recommendation that recurs across a vessel's black box entries: one row per rule and setting.
    /// </summary>
    public class BlackBoxDoctorFinding
    {
        #region Public-Members

        /// <summary>
        /// Grouping key: the rule id and the setting (for example BBX-LAND-001/LandingMode).
        /// </summary>
        public string Key { get; set; } = "";

        /// <summary>
        /// Rule id that made the recommendation.
        /// </summary>
        public string? RuleId { get; set; } = null;

        /// <summary>
        /// Recommendation id on the newest entry (apply it with apply_blackbox_recommendation).
        /// </summary>
        public string? RecommendationId { get; set; } = null;

        /// <summary>
        /// Setting the recommendation changes.
        /// </summary>
        public string Setting { get; set; } = "";

        /// <summary>
        /// Where the setting lives.
        /// </summary>
        public BlackBoxConfigSourceEnum Scope { get; set; } = BlackBoxConfigSourceEnum.Vessel;

        /// <summary>
        /// Current value on the newest entry, or null.
        /// </summary>
        public string? CurrentValue { get; set; } = null;

        /// <summary>
        /// Suggested value on the newest entry, or null.
        /// </summary>
        public string? SuggestedValue { get; set; } = null;

        /// <summary>
        /// Why, from the newest entry.
        /// </summary>
        public string? Rationale { get; set; } = null;

        /// <summary>
        /// Entries in the window that carried the recommendation.
        /// </summary>
        public int Count { get; set; } = 0;

        /// <summary>
        /// Oldest entry in the window with it.
        /// </summary>
        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Newest entry with it.
        /// </summary>
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The newest entry with it (bbx_ prefix).
        /// </summary>
        public string ExampleEntryId { get; set; } = "";

        /// <summary>
        /// The newest entry's mission, or null.
        /// </summary>
        public string? ExampleMissionId { get; set; } = null;

        /// <summary>
        /// True when the newest entry's recommendation has an apply action.
        /// </summary>
        public bool Applicable { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxDoctorFinding()
        {
        }

        #endregion
    }
}
