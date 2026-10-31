namespace Armada.Core.Services
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What kind of account usage capture a runtime has, reported by
    /// <see cref="AgentRuntimeCapabilities"/> beside the supported boolean. Describes the source, not
    /// guaranteed availability: a supported runtime still answers Unsupported, AuthMissing or Failed for
    /// an observation it cannot make.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UsageCaptureKindEnum
    {
        /// <summary>
        /// An active read of a verified account-limit source with no model prompt (a CLI-compatible OAuth
        /// endpoint or a documented app-server request).
        /// </summary>
        [EnumMember(Value = "Active")]
        Active,

        /// <summary>
        /// A passive harvest from traffic that already happens (allowlisted provider response headers);
        /// the last observation is returned without a fresh capture and is never actively requested.
        /// </summary>
        [EnumMember(Value = "Passive")]
        Passive,

        /// <summary>
        /// An operator-configured estimate (a configured budget and source revision), always labeled
        /// Estimated and never authoritative for routing.
        /// </summary>
        [EnumMember(Value = "Estimated")]
        Estimated,

        /// <summary>
        /// No verified capture source. The runtime answers Unsupported with a reason.
        /// </summary>
        [EnumMember(Value = "Unsupported")]
        Unsupported
    }
}