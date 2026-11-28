namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Chooses the persona prompt variant (<see cref="PromptVariantEnum"/>) a mission renders with. Order: the voyage's
    /// captain override for the mission's persona (an exact persona match, then the wildcard <c>*</c>), then the
    /// assigned captain's <c>promptVariant</c> runtime option (<see cref="CaptainPromptOptions"/>), then the captain's
    /// model profile rendering template (<see cref="ModelProfileRendering.Template"/>), then the served context against
    /// the automatic thresholds (Compact at or below 32768 tokens, Lean at or below 65536 by default), then
    /// <see cref="PromptVariantEnum.Current"/>. An override, option or template without a valid variant does not count.
    /// </summary>
    public static class PromptVariantResolver
    {
        #region Public-Members

        /// <summary>
        /// Default served context, in tokens, at or below which a mission renders Compact automatically (32768).
        /// </summary>
        public const int DefaultCompactMaxContextTokens = 32768;

        /// <summary>
        /// Default served context, in tokens, at or below which a mission renders Lean automatically (65536).
        /// </summary>
        public const int DefaultLeanMaxContextTokens = 65536;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the prompt variant for a mission from the voyage overrides and the captain's option only (no model
        /// profile and no automatic choice).
        /// </summary>
        /// <param name="persona">Mission persona, or null for Worker.</param>
        /// <param name="voyageOverrides">The voyage's captain overrides, or null.</param>
        /// <param name="captain">The assigned captain, or null.</param>
        /// <returns>The variant to render.</returns>
        public static PromptVariantEnum Resolve(string? persona, IEnumerable<CaptainAssignmentOverride>? voyageOverrides, Captain? captain)
        {
            return Resolve(persona, voyageOverrides, captain, null, null, 0, 0).Variant;
        }

        /// <summary>
        /// Resolve the prompt variant for a mission and what chose it.
        /// </summary>
        /// <param name="persona">Mission persona, or null for Worker.</param>
        /// <param name="voyageOverrides">The voyage's captain overrides, or null.</param>
        /// <param name="captain">The assigned captain, or null.</param>
        /// <param name="rendering">The captain's model profile rendering overrides, or null.</param>
        /// <param name="servedContextTokens">The captain's served context in tokens (<see cref="ServedContextOf"/>), or null when not known.</param>
        /// <param name="compactMaxContextTokens">Served context at or below which Compact is chosen; 0 or less turns it off.</param>
        /// <param name="leanMaxContextTokens">Served context at or below which Lean is chosen; 0 or less turns it off.</param>
        /// <returns>The resolution.</returns>
        public static PromptVariantResolution Resolve(
            string? persona,
            IEnumerable<CaptainAssignmentOverride>? voyageOverrides,
            Captain? captain,
            ModelProfileRendering? rendering,
            int? servedContextTokens,
            int compactMaxContextTokens,
            int leanMaxContextTokens)
        {
            PromptVariantEnum? fromVoyage = FromVoyageOverrides(persona, voyageOverrides);
            if (fromVoyage.HasValue) return Result(fromVoyage.Value, PromptVariantSourceEnum.VoyageOverride, null);

            PromptVariantEnum? fromCaptain = FromCaptain(captain);
            if (fromCaptain.HasValue) return Result(fromCaptain.Value, PromptVariantSourceEnum.CaptainOption, null);

            PromptVariantEnum? fromProfile = FromRendering(rendering);
            if (fromProfile.HasValue) return Result(fromProfile.Value, PromptVariantSourceEnum.ModelProfile, null);

            PromptVariantEnum? fromContext = FromServedContext(servedContextTokens, compactMaxContextTokens, leanMaxContextTokens);
            if (fromContext.HasValue) return Result(fromContext.Value, PromptVariantSourceEnum.ServedContext, servedContextTokens);

            return Result(PromptVariantEnum.Current, PromptVariantSourceEnum.Default, servedContextTokens);
        }

        /// <summary>
        /// The variant a model profile's rendering template names (case-insensitive Current, Lean or Compact), or null
        /// when the rendering is null, names none, or names an unknown variant.
        /// </summary>
        /// <param name="rendering">Rendering overrides, or null.</param>
        /// <returns>The variant, or null.</returns>
        public static PromptVariantEnum? FromRendering(ModelProfileRendering? rendering)
        {
            if (rendering == null || String.IsNullOrWhiteSpace(rendering.Template)) return null;
            try
            {
                return Parse(rendering.Template);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>
        /// The automatic variant for a served context: Compact at or below <paramref name="compactMaxContextTokens"/>,
        /// else Lean at or below <paramref name="leanMaxContextTokens"/>, else null. An unknown context chooses nothing.
        /// </summary>
        /// <param name="servedContextTokens">Served context in tokens, or null.</param>
        /// <param name="compactMaxContextTokens">Compact threshold; 0 or less turns it off.</param>
        /// <param name="leanMaxContextTokens">Lean threshold; 0 or less turns it off.</param>
        /// <returns>The variant, or null.</returns>
        public static PromptVariantEnum? FromServedContext(int? servedContextTokens, int compactMaxContextTokens, int leanMaxContextTokens)
        {
            if (!servedContextTokens.HasValue || servedContextTokens.Value <= 0) return null;
            if (compactMaxContextTokens > 0 && servedContextTokens.Value <= compactMaxContextTokens) return PromptVariantEnum.Compact;
            if (leanMaxContextTokens > 0 && servedContextTokens.Value <= leanMaxContextTokens) return PromptVariantEnum.Lean;
            return null;
        }

        /// <summary>
        /// The served context a captain runs with: its endpoint's window (configured, else discovered;
        /// <see cref="ModelContextDiscovery.WindowOf"/>), else its model profile's served context, else null.
        /// </summary>
        /// <param name="endpoint">The captain's model endpoint, or null.</param>
        /// <param name="profile">The captain's model profile, or null.</param>
        /// <returns>Served context in tokens, or null.</returns>
        public static int? ServedContextOf(ModelEndpoint? endpoint, ModelProfile? profile)
        {
            int? window = endpoint != null ? ModelContextDiscovery.WindowOf(endpoint) : null;
            if (window.HasValue && window.Value > 0) return window;
            int? served = profile?.Identity.ServedContextTokens;
            return served.HasValue && served.Value > 0 ? served : null;
        }

        /// <summary>
        /// The variant a voyage's captain overrides choose for a persona: the first exact persona match that sets a
        /// variant, else the first wildcard (<c>*</c> or empty persona) that sets one, else null.
        /// </summary>
        /// <param name="persona">Mission persona, or null for Worker.</param>
        /// <param name="voyageOverrides">The voyage's captain overrides, or null.</param>
        /// <returns>The chosen variant, or null.</returns>
        public static PromptVariantEnum? FromVoyageOverrides(string? persona, IEnumerable<CaptainAssignmentOverride>? voyageOverrides)
        {
            if (voyageOverrides == null) return null;
            string routingPersona = String.IsNullOrWhiteSpace(persona) ? PersonaCatalog.Worker : persona.Trim();

            PromptVariantEnum? wildcard = null;
            foreach (CaptainAssignmentOverride ov in voyageOverrides)
            {
                if (ov == null || !ov.PromptVariant.HasValue || !Enum.IsDefined(ov.PromptVariant.Value)) continue;
                if (String.IsNullOrEmpty(ov.Persona) || ov.Persona == "*")
                {
                    if (!wildcard.HasValue) wildcard = ov.PromptVariant;
                    continue;
                }

                if (PersonaCatalog.Matches(ov.Persona, routingPersona)) return ov.PromptVariant;
            }

            return wildcard;
        }

        /// <summary>
        /// The variant set in a captain's runtime options (<c>promptVariant</c>), or null when the captain is null, sets
        /// none, or its options are unreadable.
        /// </summary>
        /// <param name="captain">Captain, or null.</param>
        /// <returns>The captain's variant, or null.</returns>
        public static PromptVariantEnum? FromCaptain(Captain? captain)
        {
            if (captain == null || String.IsNullOrWhiteSpace(captain.RuntimeOptionsJson)) return null;
            try
            {
                PromptVariantEnum? variant = CaptainRuntimeOptions.Deserialize<CaptainPromptOptions>(captain.RuntimeOptionsJson)?.PromptVariant;
                if (variant.HasValue && !Enum.IsDefined(variant.Value)) return null;
                return variant;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Parse a variant name (case-insensitive: Current, Lean or Compact).
        /// </summary>
        /// <param name="value">Variant name, or null.</param>
        /// <returns>The variant, or null when the value is null or whitespace.</returns>
        /// <exception cref="ArgumentException">Thrown when the value names no variant.</exception>
        public static PromptVariantEnum? Parse(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string trimmed = value.Trim();
            foreach (PromptVariantEnum variant in Enum.GetValues<PromptVariantEnum>())
            {
                if (String.Equals(variant.ToString(), trimmed, StringComparison.OrdinalIgnoreCase)) return variant;
            }

            throw new ArgumentException("promptVariant must be Current, Lean or Compact.", nameof(value));
        }

        #endregion

        #region Private-Methods

        private static PromptVariantResolution Result(PromptVariantEnum variant, PromptVariantSourceEnum source, int? servedContextTokens)
        {
            return new PromptVariantResolution { Variant = variant, Source = source, ServedContextTokens = servedContextTokens };
        }

        #endregion
    }
}
