namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// What a stage submitted through a submit tool (RELIABILITY_SIMPLICITY_PLAN.md RS-6.2).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum StageSubmissionKindEnum
    {
        /// <summary>
        /// An Architect plan (<c>armada_submit_plan</c>).
        /// </summary>
        Plan,

        /// <summary>
        /// A Judge verdict (<c>armada_submit_verdict</c>).
        /// </summary>
        Verdict,

        /// <summary>
        /// A Worker, Test Engineer, Linter, Usability Engineer or Product Manager result (<c>armada_submit_result</c>).
        /// </summary>
        Result
    }
}
