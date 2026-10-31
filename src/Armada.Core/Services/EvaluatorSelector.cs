namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Picks the evaluator of a Full Evaluate (docs/MODEL_HETEROGENEITY.md, Evaluate (Full)): the highest-tier idle captain
    /// of a different model family from the candidate. A same-family evaluator is used only when no other is idle, and the
    /// report then carries a warning. With no idle captain at all the subjective grading is skipped and the reason
    /// recorded; the mechanical parts still run.
    /// </summary>
    public static class EvaluatorSelector
    {
        #region Public-Methods

        /// <summary>
        /// Pick the evaluator from the idle captains. A captain without a tier ranks as Standard; ties go to the captain
        /// with the lowest id so the choice is repeatable. The candidate itself is never its own evaluator.
        /// </summary>
        /// <param name="candidateFamily">The candidate's model family.</param>
        /// <param name="candidateCaptainId">The candidate captain's id, or null for a model endpoint.</param>
        /// <param name="idle">Idle captains of the candidate's tenant.</param>
        /// <param name="endpointOf">Reads a captain's model endpoint, or null.</param>
        /// <returns>The selection.</returns>
        /// <exception cref="ArgumentNullException">Thrown when candidateFamily, idle or endpointOf is null.</exception>
        public static EvaluatorSelection Select(string candidateFamily, string? candidateCaptainId, IEnumerable<Captain> idle, Func<Captain, ModelEndpoint?> endpointOf)
        {
            if (candidateFamily == null) throw new ArgumentNullException(nameof(candidateFamily));
            if (idle == null) throw new ArgumentNullException(nameof(idle));
            if (endpointOf == null) throw new ArgumentNullException(nameof(endpointOf));

            List<Captain> ordered = idle
                .Where(c => c != null && c.State == CaptainStateEnum.Idle)
                .Where(c => String.IsNullOrEmpty(candidateCaptainId) || !String.Equals(c.Id, candidateCaptainId, StringComparison.Ordinal))
                .Where(c => !c.QuarantineUntilUtc.HasValue || c.QuarantineUntilUtc.Value <= DateTime.UtcNow)
                .OrderByDescending(c => (int)(c.Tier ?? CaptainTierEnum.Standard))
                .ThenBy(c => c.Id, StringComparer.Ordinal)
                .ToList();

            if (ordered.Count == 0)
            {
                return new EvaluatorSelection
                {
                    SkippedReason = "No idle captain was available to act as the evaluator, so the subjective grading and the strengths and weaknesses summary were skipped; the mechanical counts are complete."
                };
            }

            foreach (Captain captain in ordered)
            {
                ModelEndpoint? endpoint = endpointOf(captain);
                string family = ModelFamily.Of(captain, endpoint);
                if (!String.Equals(family, candidateFamily, StringComparison.OrdinalIgnoreCase))
                    return new EvaluatorSelection { Captain = captain, Endpoint = endpoint, Family = family };
            }

            Captain fallback = ordered[0];
            ModelEndpoint? fallbackEndpoint = endpointOf(fallback);
            return new EvaluatorSelection
            {
                Captain = fallback,
                Endpoint = fallbackEndpoint,
                Family = ModelFamily.Of(fallback, fallbackEndpoint),
                Warning = SameFamilyWarning(fallback, candidateFamily, false)
            };
        }

        /// <summary>
        /// The selection for an evaluator the administrator named, with the same-family warning when it applies.
        /// </summary>
        /// <param name="candidateFamily">The candidate's model family.</param>
        /// <param name="evaluator">Named evaluator.</param>
        /// <param name="endpoint">Its model endpoint, or null.</param>
        /// <returns>The selection.</returns>
        /// <exception cref="ArgumentNullException">Thrown when candidateFamily or evaluator is null.</exception>
        public static EvaluatorSelection Named(string candidateFamily, Captain evaluator, ModelEndpoint? endpoint)
        {
            if (candidateFamily == null) throw new ArgumentNullException(nameof(candidateFamily));
            if (evaluator == null) throw new ArgumentNullException(nameof(evaluator));
            string family = ModelFamily.Of(evaluator, endpoint);
            return new EvaluatorSelection
            {
                Captain = evaluator,
                Endpoint = endpoint,
                Family = family,
                Explicit = true,
                Warning = String.Equals(family, candidateFamily, StringComparison.OrdinalIgnoreCase) ? SameFamilyWarning(evaluator, candidateFamily, true) : null
            };
        }

        #endregion

        #region Private-Methods

        private static string SameFamilyWarning(Captain evaluator, string family, bool named)
        {
            return "The evaluator " + evaluator.Name + " is of the same model family (" + family + ") as the candidate"
                + (named ? " (named by the administrator)" : " because no idle captain of another family was available")
                + "; its subjective grading may share the candidate's blind spots.";
        }

        #endregion
    }
}
