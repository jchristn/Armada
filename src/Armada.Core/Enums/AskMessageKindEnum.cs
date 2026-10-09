namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Kind of an Ask Armada thread message, which determines how it is rendered.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AskMessageKindEnum
    {
        /// <summary>
        /// Plain conversational text (markdown).
        /// </summary>
        [EnumMember(Value = "Text")]
        Text,

        /// <summary>
        /// A state-changing tool call waiting for the user's approval (confirm card).
        /// </summary>
        [EnumMember(Value = "ActionProposal")]
        ActionProposal,

        /// <summary>
        /// The outcome of an executed action.
        /// </summary>
        [EnumMember(Value = "ActionResult")]
        ActionResult,

        /// <summary>
        /// A milestone of tracked work (started, mission failed, landed, done).
        /// </summary>
        [EnumMember(Value = "WorkUpdate")]
        WorkUpdate,

        /// <summary>
        /// A summary of the conversation so far.
        /// </summary>
        [EnumMember(Value = "Summary")]
        Summary,

        /// <summary>
        /// A failed turn or action.
        /// </summary>
        [EnumMember(Value = "Error")]
        Error,

        /// <summary>
        /// A CLI permission prompt of the thread's captain waiting for (or decided by) an approver (permission card).
        /// </summary>
        [EnumMember(Value = "CliPermission")]
        CliPermission,

        /// <summary>
        /// The captain's report of the outcome of work this conversation started, written by an automatic follow-up turn
        /// when the work finishes (Ask.ReportResultsOnCompletion). Clients render it like a captain reply with a "Report"
        /// label; clients that do not know this kind render it as plain assistant text.
        /// </summary>
        [EnumMember(Value = "WorkReport")]
        WorkReport
    }
}
