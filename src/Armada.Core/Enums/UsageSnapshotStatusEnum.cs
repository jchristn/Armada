namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Outcome of one usage-capture attempt recorded in a UsageSnapshot. Ok means fresh authoritative or
    /// accepted observed data; Estimated means Armada derived the numbers from configured budgets with
    /// incomplete coverage; AuthMissing means the credential host could not read a usable identity;
    /// Unsupported means the runtime has no verified account-limit source; Failed means the attempt errored
    /// (timeout, malformed output, host unavailable). Only Ok and Estimated carry utilization windows.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UsageSnapshotStatusEnum
    {
        /// <summary>
        /// Fresh observed account-limit data was captured.
        /// </summary>
        [EnumMember(Value = "Ok")]
        Ok,

        /// <summary>
        /// The runtime has no verified account-limit source for this configuration.
        /// </summary>
        [EnumMember(Value = "Unsupported")]
        Unsupported,

        /// <summary>
        /// The credential host could not read a usable account identity; polling should back off slowly.
        /// </summary>
        [EnumMember(Value = "AuthMissing")]
        AuthMissing,

        /// <summary>
        /// The attempt errored (malformed output, timeout, host unavailable); last good data is retained.
        /// </summary>
        [EnumMember(Value = "Failed")]
        Failed,

        /// <summary>
        /// Numbers are Armada-derived estimates with incomplete coverage; they never quarantine a captain.
        /// </summary>
        [EnumMember(Value = "Estimated")]
        Estimated
    }
}