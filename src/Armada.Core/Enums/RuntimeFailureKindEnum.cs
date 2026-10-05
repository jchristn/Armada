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
        /// A non-zero exit with no structured provider error: treat as a real crash.
        /// </summary>
        [EnumMember(Value = "Crash")]
        Crash,

        /// <summary>
        /// The provider reported that the requested model does not exist or is not available to these
        /// credentials. The captain cannot make progress until its model configuration is fixed.
        /// </summary>
        [EnumMember(Value = "ModelUnavailable")]
        ModelUnavailable
    }
}
