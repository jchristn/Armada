namespace Armada.Core.Services
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What a usage observation asks of a captain's quota-owned routing hold (decision D6).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UsageQuotaActionEnum
    {
        /// <summary>
        /// The observation does not affect routing: not authoritative, stale, for another account, or without an applicable window.
        /// </summary>
        [EnumMember(Value = "NoChange")]
        NoChange,

        /// <summary>
        /// A fresh authoritative applicable window is exhausted: hold the captain out of dispatch until it resets.
        /// </summary>
        [EnumMember(Value = "Hold")]
        Hold,

        /// <summary>
        /// Every fresh authoritative applicable window is below its limit: release a quota-owned hold only.
        /// </summary>
        [EnumMember(Value = "Release")]
        Release
    }
}
