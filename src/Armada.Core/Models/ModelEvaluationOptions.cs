namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Parameters of a Full Evaluate (docs/MODEL_HETEROGENEITY.md, Evaluate (Full)). Every value is optional.
    /// </summary>
    public class ModelEvaluationOptions
    {
        #region Public-Members

        /// <summary>
        /// Evaluator captain for the subjective grading (cpt_ prefix), or null for the default: the highest-tier idle
        /// captain of a different model family from the candidate.
        /// </summary>
        public string? EvaluatorCaptainId { get; set; } = null;

        /// <summary>
        /// Battery packs to run, or null or empty for the packs matching the languages of the tenant's vessels.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string>? Packs { get; set; } = null;

        /// <summary>
        /// Personas to score, or null or empty for every persona the captain is allowed.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string>? Personas { get; set; } = null;

        /// <summary>
        /// Runs of each battery task, or null for 3 (1 to 10).
        /// </summary>
        public int? RunCount { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelEvaluationOptions()
        {
        }

        #endregion
    }
}
