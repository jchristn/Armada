namespace Armada.Core.Models
{
    /// <summary>
    /// The evaluator a Full Evaluate picked for its subjective grading, or why there is none.
    /// </summary>
    public class EvaluatorSelection
    {
        #region Public-Members

        /// <summary>
        /// The evaluator captain, or null when none is available.
        /// </summary>
        public Captain? Captain { get; set; } = null;

        /// <summary>
        /// The evaluator's model endpoint, or null.
        /// </summary>
        public ModelEndpoint? Endpoint { get; set; } = null;

        /// <summary>
        /// The evaluator's model family, or null.
        /// </summary>
        public string? Family { get; set; } = null;

        /// <summary>
        /// True when the administrator named the evaluator.
        /// </summary>
        public bool Explicit { get; set; } = false;

        /// <summary>
        /// Warning for the report (a same-family evaluator), or null.
        /// </summary>
        public string? Warning { get; set; } = null;

        /// <summary>
        /// Why there is no evaluator, or null when there is one.
        /// </summary>
        public string? SkippedReason { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public EvaluatorSelection()
        {
        }

        #endregion
    }
}
