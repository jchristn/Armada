namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The Judge strength rule (RS-8.7) and the model identity Judge author exclusion compares. A Judge must have an
    /// effective tier at least the highest tier among the captains that ran the stages it reviews; among those, a captain
    /// of a different <see cref="ModelFamily"/> than every author is preferred. Side-effect free.
    /// </summary>
    public static class JudgeStrengthRule
    {
        #region Public-Members

        /// <summary>
        /// Selection reason code of a blocker whose available captains are all below the reviewed work's tier.
        /// </summary>
        public const string BlockerCode = "judge_strength";

        /// <summary>
        /// Event type recorded when a ProceedAndFlag pipeline's Judge runs below the reviewed work's tier (gate G8).
        /// </summary>
        public const string ExceptionEventType = "mission.judge_strength_exception";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse a stored policy value; null for DBNull, null, empty or unknown text.
        /// </summary>
        /// <param name="value">Stored value.</param>
        /// <returns>The policy, or null.</returns>
        public static JudgeStrengthPolicyEnum? Parse(object? value)
        {
            if (value == null || value is DBNull) return null;
            string? text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            if (String.IsNullOrWhiteSpace(text)) return null;
            return Enum.TryParse(text.Trim(), true, out JudgeStrengthPolicyEnum policy) && Enum.IsDefined(policy) ? policy : null;
        }

        /// <summary>
        /// The policy in effect for a voyage: its stored value, else Off.
        /// </summary>
        /// <param name="voyage">Voyage, or null.</param>
        /// <returns>The policy.</returns>
        public static JudgeStrengthPolicyEnum Effective(Voyage? voyage)
        {
            return voyage?.JudgeStrength ?? JudgeStrengthPolicyEnum.Off;
        }

        /// <summary>
        /// The tier a Judge needs: the highest effective tier among the authors, or null when there are none.
        /// </summary>
        /// <param name="authors">Captains that ran the reviewed stages.</param>
        /// <returns>The tier, or null.</returns>
        /// <exception cref="ArgumentNullException">The authors are null.</exception>
        public static CaptainTierEnum? RequiredTier(IEnumerable<Captain> authors)
        {
            if (authors == null) throw new ArgumentNullException(nameof(authors));
            List<Captain> list = authors.Where(a => a != null).ToList();
            if (list.Count == 0) return null;
            return list.Max(CaptainTierSelector.EffectiveTier);
        }

        /// <summary>
        /// Whether a captain meets the rule for a required tier (null meets it).
        /// </summary>
        /// <param name="captain">Captain.</param>
        /// <param name="required">Required tier, or null.</param>
        /// <returns>True when the captain may judge under the rule.</returns>
        /// <exception cref="ArgumentNullException">The captain is null.</exception>
        public static bool Meets(Captain captain, CaptainTierEnum? required)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            return !required.HasValue || CaptainTierSelector.CanHandle(captain, required.Value);
        }

        /// <summary>
        /// The model identity of a captain, used to exclude review authors by model rather than by captain: its model name
        /// (the captain's, else its endpoint's), lower case, without a provider path prefix, whatever runtime serves it. A
        /// captain with no model runs its runtime's default model and is identified by the runtime alone.
        /// </summary>
        /// <param name="captain">Captain.</param>
        /// <param name="endpoint">The captain's model endpoint, or null.</param>
        /// <returns>The identity.</returns>
        /// <exception cref="ArgumentNullException">The captain is null.</exception>
        public static string ModelIdentity(Captain captain, ModelEndpoint? endpoint)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            string? model = !String.IsNullOrWhiteSpace(captain.Model) ? captain.Model : endpoint?.Model;
            if (String.IsNullOrWhiteSpace(model)) return "default:" + captain.Runtime.ToString().ToLowerInvariant();
            string name = model.Trim().ToLowerInvariant();
            int slash = name.LastIndexOf('/');
            if (slash >= 0 && slash < name.Length - 1) name = name.Substring(slash + 1);
            return "model:" + name;
        }

        #endregion
    }
}
