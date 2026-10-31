namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// Request to start an Evaluate of a captain or a model endpoint. Exactly one of <see cref="CaptainId"/> and
    /// <see cref="ModelEndpointId"/> is set. The inherited Full Evaluate parameters (evaluator, packs, personas, run count)
    /// apply only to depth Full.
    /// </summary>
    public class EvaluateCaptainRequest : ModelEvaluationOptions
    {
        #region Public-Members

        /// <summary>
        /// Captain to evaluate through its own runtime, or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Model endpoint to evaluate through the API endpoint runtime, or null.
        /// </summary>
        public string? ModelEndpointId { get; set; } = null;

        /// <summary>
        /// Depth; default Quick (the mechanical probes). Full adds the fixture battery, the evaluator's subjective grading and
        /// an advisory model profile revision carrying the report; it runs only when asked for.
        /// </summary>
        public ModelProbeDepthEnum Depth { get; set; } = ModelProbeDepthEnum.Quick;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public EvaluateCaptainRequest()
        {
        }

        #endregion
    }
}
