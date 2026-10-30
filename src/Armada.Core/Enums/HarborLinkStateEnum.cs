namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// State of the Admiral's link to a Harbor at one moment (HarborConnectionManager.GetLinkState), recorded in a black
    /// box entry for Harbor-hosted docks.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborLinkStateEnum
    {
        /// <summary>
        /// The Harbor is linked and was heard from within its heartbeat window.
        /// </summary>
        [EnumMember(Value = "Linked")]
        Linked,

        /// <summary>
        /// The Harbor is linked but has been silent for more than half of its stale-link timeout.
        /// </summary>
        [EnumMember(Value = "Silent")]
        Silent,

        /// <summary>
        /// The Harbor has no live link to this Admiral.
        /// </summary>
        [EnumMember(Value = "NotLinked")]
        NotLinked,

        /// <summary>
        /// The link state could not be determined.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
