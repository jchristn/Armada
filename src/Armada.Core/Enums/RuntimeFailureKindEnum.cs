namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Classification of why a captain runtime process ended, derived from its exit code and the structured
    /// provider error (HTTP status, provider error type) the runtime reported. Distinguishes a genuine crash from a
    /// provider condition so downstream policy (quarantine, stall, redispatch) can react appropriately.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RuntimeFailureKindEnum
    {
        /// <summary>
        /// The process exited successfully (exit code 0) with no failure signature.
        /// </summary>
        [EnumMember(Value = "Clean")]
        Clean,

        /// <summary>
        /// The provider refused the request for quota / rate-limit / credit / billing reasons. Retrying
        /// immediately will fail again; the captain should be quarantined until the limit resets.
        /// </summary>
        [EnumMember(Value = "UsageLimit")]
        UsageLimit,

        /// <summary>
        /// The provider rejected the credentials (unauthorized / invalid API key / forbidden). The captain
        /// cannot make progress until its configuration is fixed.
        /// </summary>
        [EnumMember(Value = "AuthFailure")]
        AuthFailure,

        /// <summary>
        /// The process died abnormally: it was killed by a signal (exit code 129 to 192 on Linux and macOS), ended with
        /// a negative exit code (a Windows crash status), or the runtime reported it was cancelled.
        /// </summary>
        [EnumMember(Value = "Crash")]
        Crash,

        /// <summary>
        /// The provider reported that the requested model does not exist or is not available to these
        /// credentials. The captain cannot make progress until its model configuration is fixed.
        /// </summary>
        [EnumMember(Value = "ModelUnavailable")]
        ModelUnavailable,

        /// <summary>
        /// The agent ran but could not finish the task: it reached its iteration or token budget, degenerated into
        /// repetition, or outgrew its context window. The mission fails, but the captain is healthy, so it does not
        /// count toward crash-loop quarantine.
        /// </summary>
        [EnumMember(Value = "TaskFailure")]
        TaskFailure,

        /// <summary>
        /// The model endpoint failed in a way that usually clears on the next request: a 5xx response (for example a
        /// local server that could not parse the model's tool call), a dropped connection, or a timeout. The captain
        /// is relaunched in the same dock instead of failing the mission.
        /// </summary>
        [EnumMember(Value = "Transient")]
        Transient,

        /// <summary>
        /// A non-zero exit that is not an abnormal death and whose provider error is missing, or carries neither a known
        /// error type nor a mapped HTTP status (RS-5.2). Recovery gives it one resume in place, then hands the mission to
        /// escalation. Counted per runtime and model by acceptance gate G6.
        /// </summary>
        [EnumMember(Value = "Unclassified")]
        Unclassified
    }
}
