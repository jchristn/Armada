namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Pure tier/capability routing for dispatch. Given a set of already-eligible idle captains and a
    /// mission's required tier, resolve the captain to assign: prefer the lowest tier that can still
    /// handle the work (so strong captains are not consumed by cheap missions), falling back UPWARD only
    /// when no captain at the required tier is idle. A captain's effective tier is its explicit
    /// <see cref="Captain.Tier"/> when set, otherwise auto-classified from its model name, otherwise
    /// <see cref="CaptainTierEnum.Standard"/>. Side-effect free so it can be unit tested in isolation.
    /// </summary>
    public static class CaptainTierSelector
    {
        #region Private-Members

        // Anchored model-family patterns -> tier. Routine version bumps auto-register without config edits.
        private static readonly (Regex Pattern, CaptainTierEnum Tier)[] _ModelFamilies = new (Regex, CaptainTierEnum)[]
        {
            (new Regex(@"opus|gpt-?5(\.\d+)?($|[^-])|o3|o4|gemini-\d*.*pro", RegexOptions.IgnoreCase | RegexOptions.Compiled), CaptainTierEnum.Premium),
            // \bmini avoids matching "geMINI"; the others are distinctive enough to leave unanchored.
            (new Regex(@"haiku|\bmini|nano|flash|gpt-?oss|small|-lite", RegexOptions.IgnoreCase | RegexOptions.Compiled), CaptainTierEnum.Economy),
            // OpenCode / OpenAI-compatible open model families default to Standard unless matched as economy above.
            (new Regex(@"sonnet|gpt-?4|gemini-\d*.*(?!pro)|glm|qwen|kimi|deepseek|\bllama|mistral|codestral|devstral|command-?r", RegexOptions.IgnoreCase | RegexOptions.Compiled), CaptainTierEnum.Standard),
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// The effective tier for a captain: explicit tier, else classified from the model, else Standard.
        /// </summary>
        /// <param name="captain">The captain.</param>
        /// <returns>The resolved tier.</returns>
        public static CaptainTierEnum EffectiveTier(Captain captain)
        {
            if (captain == null) return CaptainTierEnum.Standard;
            if (captain.Tier.HasValue) return captain.Tier.Value;
            return ClassifyModel(captain.Model);
        }

        /// <summary>
        /// Classify a model name into a tier via anchored family patterns; Standard when unknown.
        /// </summary>
        /// <param name="model">The model identifier, or null.</param>
        /// <returns>The classified tier.</returns>
        public static CaptainTierEnum ClassifyModel(string? model)
        {
            if (String.IsNullOrWhiteSpace(model)) return CaptainTierEnum.Standard;
            // Economy and Premium patterns are checked before the broad Standard fallback.
            if (_ModelFamilies[1].Pattern.IsMatch(model)) return CaptainTierEnum.Economy;
            if (_ModelFamilies[0].Pattern.IsMatch(model)) return CaptainTierEnum.Premium;
            return CaptainTierEnum.Standard;
        }

        /// <summary>
        /// Choose a captain for a mission of the given required tier from the supplied idle, already
        /// persona-eligible captains. Returns null if no idle captain can handle the required tier.
        /// </summary>
        /// <param name="eligibleIdleCaptains">Idle captains already filtered by persona.</param>
        /// <param name="requiredTier">The mission's required tier; null means Standard.</param>
        /// <returns>The chosen captain, or null.</returns>
        public static Captain? Select(IReadOnlyList<Captain> eligibleIdleCaptains, CaptainTierEnum? requiredTier)
        {
            if (eligibleIdleCaptains == null || eligibleIdleCaptains.Count == 0) return null;
            CaptainTierEnum required = requiredTier ?? CaptainTierEnum.Standard;

            // Only captains that can handle the work (effective tier >= required) are candidates.
            List<Captain> candidates = eligibleIdleCaptains
                .Where(c => EffectiveTier(c) >= required)
                .ToList();
            if (candidates.Count == 0) return null;

            // Prefer the lowest eligible tier at/above required (upward-only fallback) so a Premium captain
            // is not consumed by Economy/Standard work while a cheaper captain is idle.
            CaptainTierEnum bestTier = candidates.Min(EffectiveTier);
            return candidates.First(c => EffectiveTier(c) == bestTier);
        }

        #endregion
    }
}
