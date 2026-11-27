namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// A model profile's tier (RS-8.5): the tier an administrator set, and the tier its outcome data suggests. Routing uses
    /// an explicit captain tier first, then <see cref="Tier"/>, then the model name. A suggestion is never applied on its
    /// own; an administrator applies it from the TierSuggestion alert or by setting <see cref="Tier"/>.
    /// </summary>
    public class ModelProfileTiering
    {
        #region Public-Members

        /// <summary>
        /// Tier set by an administrator, or null to classify captains on this profile from their model name. Editable.
        /// </summary>
        public CaptainTierEnum? Tier { get; set; } = null;

        /// <summary>
        /// Tier the outcome data suggests, or null when there is no suggestion. Read-only; written by the model alert pass
        /// when RoutingData.SuggestTiers is on.
        /// </summary>
        public CaptainTierEnum? SuggestedTier { get; set; } = null;

        /// <summary>
        /// Why the tier was suggested (score, samples, half-life), or null. Read-only.
        /// </summary>
        public string? SuggestedReason { get; set; } = null;

        /// <summary>
        /// Attempts the suggestion is based on, or null. Read-only.
        /// </summary>
        public int? SuggestedSamples { get; set; } = null;

        /// <summary>
        /// When the suggestion was last computed (UTC), or null. Read-only.
        /// </summary>
        public DateTime? SuggestedUtc { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfileTiering()
        {
        }

        #endregion
    }
}
