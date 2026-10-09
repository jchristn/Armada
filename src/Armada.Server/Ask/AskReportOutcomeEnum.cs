namespace Armada.Server.Ask
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What happened to a queued report of finished tracked work when the coordinator tried to start it.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AskReportOutcomeEnum
    {
        /// <summary>
        /// The report turn started.
        /// </summary>
        [EnumMember(Value = "Started")]
        Started,

        /// <summary>
        /// The report is not needed (setting off, thread archived or deleted or without a captain, the user already
        /// posted a message after the work finished, or a report for the work already exists).
        /// </summary>
        [EnumMember(Value = "Skipped")]
        Skipped,

        /// <summary>
        /// Another turn is running in the thread; the report waits for it to end.
        /// </summary>
        [EnumMember(Value = "Busy")]
        Busy
    }
}
