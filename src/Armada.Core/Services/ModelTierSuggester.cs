namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Suggests a model profile tier from its outcome scores (RS-8.5). Only personas with at least the minimum samples
    /// count; their scores are pooled weighted by samples. A pooled score of at least <see cref="PremiumScore"/> suggests
    /// Premium, at least <see cref="StandardScore"/> Standard, otherwise Economy. A suggestion is only made when it differs
    /// from the tier routing uses today. Advisory: nothing here changes routing. Side-effect free.
    /// </summary>
    public static class ModelTierSuggester
    {
        #region Public-Members

        /// <summary>
        /// Pooled score at or above which Premium is suggested (0.85).
        /// </summary>
        public const double PremiumScore = 0.85;

        /// <summary>
        /// Pooled score at or above which Standard is suggested (0.65).
        /// </summary>
        public const double StandardScore = 0.65;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The suggestion for a profile, or null when there is not enough data or the data agrees with the current tier.
        /// </summary>
        /// <param name="scores">The profile's scores, one per persona.</param>
        /// <param name="minimumSamples">Samples a persona needs to count.</param>
        /// <param name="halfLifeDays">Half-life the scores used, for the reason text.</param>
        /// <param name="currentTier">The tier routing uses for the profile today.</param>
        /// <returns>A tiering with the suggestion fields set (not <see cref="ModelProfileTiering.SuggestedUtc"/>), or null.</returns>
        /// <exception cref="ArgumentNullException">The scores are null.</exception>
        public static ModelProfileTiering? Suggest(IEnumerable<PersonaOutcomeScore> scores, int minimumSamples, int halfLifeDays, CaptainTierEnum currentTier)
        {
            if (scores == null) throw new ArgumentNullException(nameof(scores));
            List<PersonaOutcomeScore> counted = scores.Where(s => s != null && s.Samples >= minimumSamples).ToList();
            int samples = counted.Sum(s => s.Samples);
            if (counted.Count == 0 || samples < minimumSamples) return null;
            double pooled = counted.Sum(s => s.Score * s.Samples) / samples;
            CaptainTierEnum suggested = TierFor(pooled);
            if (suggested == currentTier) return null;
            string personas = String.Join(", ", counted.OrderByDescending(s => s.Samples).Select(s => s.Persona + " " + Percent(s.Score) + " (" + s.Samples + ")"));
            return new ModelProfileTiering
            {
                SuggestedTier = suggested,
                SuggestedSamples = samples,
                SuggestedReason = "Outcome score " + Percent(pooled) + " over " + samples + " attempts (" + personas + "; " + halfLifeDays
                    + "-day half-life) suggests " + suggested + " instead of " + currentTier + ". Premium needs " + Percent(PremiumScore)
                    + ", Standard " + Percent(StandardScore) + "."
            };
        }

        /// <summary>
        /// The tier a pooled score maps to.
        /// </summary>
        /// <param name="score">Score, 0 to 1.</param>
        /// <returns>The tier.</returns>
        public static CaptainTierEnum TierFor(double score)
        {
            if (score >= PremiumScore) return CaptainTierEnum.Premium;
            if (score >= StandardScore) return CaptainTierEnum.Standard;
            return CaptainTierEnum.Economy;
        }

        #endregion

        #region Private-Methods

        private static string Percent(double value)
        {
            return (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        #endregion
    }
}
