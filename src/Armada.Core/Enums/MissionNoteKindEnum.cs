namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Kind of note Armada attaches to a mission after it is created. A mission holds at most one note of each kind, and
    /// notes are rendered after the description when the mission launches; the description itself never changes.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MissionNoteKindEnum
    {
        /// <summary>What the previous pipeline stage handed this stage.</summary>
        PipelineHandoff,

        /// <summary>Guidance from a conditional approval of the previous stage.</summary>
        ReviewerGuidance,

        /// <summary>Feedback from a denied review of this mission.</summary>
        ReviewFeedback,

        /// <summary>The previous Judge findings, kept when a Judge stage is restarted.</summary>
        PriorJudgeFindings,

        /// <summary>The output contract repair request for the current repair attempt.</summary>
        OutputRepair,

        /// <summary>Why earlier attempts stopped, for a captain resuming the work in the same dock.</summary>
        Resume,

        /// <summary>Why the mission was escalated to a stronger captain.</summary>
        Escalation,

        /// <summary>Which sibling slices were merged into an integration mission's branch, and which were not.</summary>
        IntegrationMerge
    }
}
