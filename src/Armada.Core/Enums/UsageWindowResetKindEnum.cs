namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// How a provider window frees capacity, as declared by the capture adapter that produced it. Only
    /// <see cref="Fixed"/> windows with an absolute reset time give reservations a reset generation; every other
    /// window keeps conservative continuous accounting.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UsageWindowResetKindEnum
    {
        /// <summary>The adapter does not know; accounting stays continuous.</summary>
        [EnumMember(Value = "Unknown")]
        Unknown,

        /// <summary>Capacity replenishes continuously (token bucket or sliding window); accounting stays continuous.</summary>
        [EnumMember(Value = "Rolling")]
        Rolling,

        /// <summary>The whole window resets at the reported absolute time, which stays fixed until it passes.</summary>
        [EnumMember(Value = "Fixed")]
        Fixed
    }
}
