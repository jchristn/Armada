namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// Whether a captain's served context can hold a mission's prompt (RS-8.4), from hard facts only. The prompt is the
    /// parts the instructions composer never trims (the mission description, the persona's output contract, and
    /// <see cref="FixedAllowanceTokens"/> for the role, rules and progress signals) plus, for a stage that reads an earlier
    /// stage's handoff, the handoff's share of the context (<see cref="HandoffRenderer.ContextDivisor"/>). The prompt must
    /// fit in the context less a quarter kept for tool output and the reply. An unknown context always fits.
    /// </summary>
    public static class MissionPromptFit
    {
        #region Public-Members

        /// <summary>
        /// Tokens allowed for the untrimmed role, rules and progress signals (the composer's minimum budget, 2048).
        /// </summary>
        public const long FixedAllowanceTokens = InstructionsComposer.MinimumBudgetTokens;

        /// <summary>
        /// The share of the served context the prompt may use, in percent (75).
        /// </summary>
        public const int UsablePercent = 75;

        /// <summary>
        /// Selection reason code of a blocker whose available captains all serve too small a context.
        /// </summary>
        public const string BlockerCode = "context_too_small";

        #endregion

        #region Public-Methods

        /// <summary>
        /// The served context of a captain: its endpoint's configured context, else the context the endpoint's server
        /// reported (RS-7.8), else the model profile's served context. Null when none is known.
        /// </summary>
        /// <param name="endpoint">The captain's model endpoint, or null.</param>
        /// <param name="profile">The captain's model profile, or null.</param>
        /// <returns>Tokens, or null.</returns>
        public static int? ServedContext(ModelEndpoint? endpoint, ModelProfile? profile)
        {
            int? configured = endpoint?.InferenceOptions?.ContextLength;
            if (configured.HasValue && configured.Value > 0) return configured.Value;
            if (endpoint?.DiscoveredContextLength.HasValue == true && endpoint.DiscoveredContextLength.Value > 0) return endpoint.DiscoveredContextLength.Value;
            int? served = profile?.Identity.ServedContextTokens;
            return served.HasValue && served.Value > 0 ? served.Value : null;
        }

        /// <summary>
        /// Estimated tokens of a mission's untrimmed prompt parts, without the handoff.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <returns>Tokens.</returns>
        /// <exception cref="ArgumentNullException">The mission is null.</exception>
        public static long FixedTokens(Mission mission)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            return InstructionsComposer.EstimateTokens(mission.Description)
                + InstructionsComposer.EstimateTokens(StageOutputContract.For(mission.Persona))
                + FixedAllowanceTokens;
        }

        /// <summary>
        /// Estimated prompt tokens for a mission read in a context of the given size.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="contextTokens">Served context.</param>
        /// <returns>Tokens.</returns>
        /// <exception cref="ArgumentNullException">The mission is null.</exception>
        public static long EstimateTokens(Mission mission, int contextTokens)
        {
            long handoff = String.IsNullOrEmpty(mission?.DependsOnMissionId) ? 0 : contextTokens / HandoffRenderer.ContextDivisor;
            return FixedTokens(mission!) + handoff;
        }

        /// <summary>
        /// Why a served context cannot hold the mission's prompt, or null when it can (or is unknown).
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="contextTokens">Served context, or null when unknown.</param>
        /// <returns>A short reason, or null.</returns>
        /// <exception cref="ArgumentNullException">The mission is null.</exception>
        public static string? IneligibilityReason(Mission mission, int? contextTokens)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (!contextTokens.HasValue || contextTokens.Value <= 0) return null;
            long needed = EstimateTokens(mission, contextTokens.Value);
            long usable = (long)contextTokens.Value * UsablePercent / 100;
            if (needed <= usable) return null;
            return "served context " + contextTokens.Value + " tokens cannot hold the prompt (about " + needed
                + " tokens with the description, output contract and handoff; " + usable + " usable)";
        }

        #endregion
    }
}
