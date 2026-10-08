namespace Armada.Core.Connectivity
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// How a <see cref="UrlProbeStep"/> ended.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UrlProbeStepStatusEnum
    {
        /// <summary>
        /// The step succeeded.
        /// </summary>
        [EnumMember(Value = "Passed")]
        Passed,

        /// <summary>
        /// The step failed; later steps did not run.
        /// </summary>
        [EnumMember(Value = "Failed")]
        Failed,

        /// <summary>
        /// The step does not apply to this URL (TLS for http, DNS for an IP address).
        /// </summary>
        [EnumMember(Value = "Skipped")]
        Skipped
    }
}
