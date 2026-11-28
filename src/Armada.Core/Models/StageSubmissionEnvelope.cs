namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

    /// <summary>
    /// A validated stage submission: exactly one of <see cref="Plan"/>, <see cref="Verdict"/> and <see cref="Result"/> is
    /// set, matching <see cref="Kind"/>. This is the payload stored on the attempt and written into the stage output.
    /// </summary>
    public class StageSubmissionEnvelope
    {
        /// <summary>
        /// What was submitted.
        /// </summary>
        [JsonPropertyName("kind")]
        public StageSubmissionKindEnum Kind { get; set; } = StageSubmissionKindEnum.Result;

        /// <summary>
        /// The Architect plan, for <see cref="StageSubmissionKindEnum.Plan"/>.
        /// </summary>
        [JsonPropertyName("plan")]
        public ArchitectPlan? Plan { get; set; } = null;

        /// <summary>
        /// The Judge verdict, for <see cref="StageSubmissionKindEnum.Verdict"/>.
        /// </summary>
        [JsonPropertyName("verdict")]
        public StageVerdictSubmission? Verdict { get; set; } = null;

        /// <summary>
        /// The stage result, for <see cref="StageSubmissionKindEnum.Result"/>.
        /// </summary>
        [JsonPropertyName("result")]
        public StageResultSubmission? Result { get; set; } = null;
    }
}
