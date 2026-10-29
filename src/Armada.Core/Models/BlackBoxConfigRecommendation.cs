namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A configuration change that could have prevented the failure.
    /// </summary>
    public class BlackBoxConfigRecommendation
    {
        /// <summary>
        /// Recommendation id within the entry (for example cr1).
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// Setting.
        /// </summary>
        public string Setting { get; set; } = "";

        /// <summary>
        /// Where to change it.
        /// </summary>
        public BlackBoxConfigSourceEnum Scope { get; set; } = BlackBoxConfigSourceEnum.Vessel;

        /// <summary>
        /// Current value, or null.
        /// </summary>
        public string? CurrentValue { get; set; } = null;

        /// <summary>
        /// Suggested value, or null.
        /// </summary>
        public string? SuggestedValue { get; set; } = null;

        /// <summary>
        /// Why.
        /// </summary>
        public string? Rationale { get; set; } = null;

        /// <summary>
        /// Rule that produced it.
        /// </summary>
        public string? RuleId { get; set; } = null;

        /// <summary>
        /// Human instructions.
        /// </summary>
        public string? HowToApply { get; set; } = null;

        /// <summary>
        /// Machine-readable action, or null.
        /// </summary>
        public BlackBoxAction? ApplyAction { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxConfigRecommendation()
        {
        }
    }
}
