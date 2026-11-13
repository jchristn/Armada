namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What happened to the evaluator's narrative (subjective grades, strengths and weaknesses) in a Full Evaluate.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ModelEvaluationNarrativeStatusEnum
    {
        /// <summary>
        /// No evaluator ran (none available, or the run stopped before grading); the reason says why.
        /// </summary>
        [EnumMember(Value = "Skipped")]
        Skipped,

        /// <summary>
        /// The evaluator's output parsed and every count it cites matches the mechanical counts.
        /// </summary>
        [EnumMember(Value = "Accepted")]
        Accepted,

        /// <summary>
        /// The evaluator's output did not parse or cites counts that do not match; the mechanical report stands alone.
        /// </summary>
        [EnumMember(Value = "Rejected")]
        Rejected
    }
}
