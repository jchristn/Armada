namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Result of the most recent health check for a model endpoint (or the base URL it shares).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EndpointHealthStatusEnum
    {
        /// <summary>
        /// Not yet health-checked.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown,

        /// <summary>
        /// The base URL responded successfully to the last probe.
        /// </summary>
        [EnumMember(Value = "Healthy")]
        Healthy,

        /// <summary>
        /// The base URL failed (unreachable, timeout, or an error status) on the last probe.
        /// </summary>
        [EnumMember(Value = "Unhealthy")]
        Unhealthy
    }
}
