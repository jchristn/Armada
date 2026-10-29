namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// One configuration recommendation on the Ask black box card.
    /// </summary>
    public class AskBlackBoxRecommendation
    {
        #region Public-Members

        /// <summary>
        /// Recommendation id (the recommendationId of apply_blackbox_recommendation).
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = value ?? String.Empty;
        }

        /// <summary>
        /// Setting the recommendation changes.
        /// </summary>
        public string Setting
        {
            get => _Setting;
            set => _Setting = value ?? String.Empty;
        }

        /// <summary>
        /// Where the setting lives.
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
        /// Why, or null.
        /// </summary>
        public string? Rationale { get; set; } = null;

        /// <summary>
        /// Rule that produced it, or null.
        /// </summary>
        public string? RuleId { get; set; } = null;

        /// <summary>
        /// Whether Armada can apply it (the entry records an apply action); otherwise HowToApply says how.
        /// </summary>
        public bool CanApply { get; set; } = false;

        /// <summary>
        /// How to apply it by hand, or null.
        /// </summary>
        public string? HowToApply { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = String.Empty;
        private string _Setting = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskBlackBoxRecommendation()
        {
        }

        #endregion
    }
}
