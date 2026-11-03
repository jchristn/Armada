namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>Comparable headroom bands; lower values rank first.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UsageSelectionBandEnum
    {
        /// <summary>Fresh applicable evidence with comfortable launch headroom.</summary>
        Comfortable = 0,
        /// <summary>Fresh evidence permits launch above the protected reserve.</summary>
        Constrained = 1,
        /// <summary>Evidence is absent, stale, unsupported or uncertain.</summary>
        Unknown = 2,
        /// <summary>An eligibility, cooldown, quota or reserve gate prevents launch.</summary>
        Blocked = 3,
    }
}
