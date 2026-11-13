namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>Usage-aware admission rollout mode.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UsageSelectionModeEnum
    {
        /// <summary>Preserve legacy routing without new decision history.</summary>
        Disabled = 0,
        /// <summary>Record actual and hypothetical decisions without enforcing usage reserves.</summary>
        Observe = 1,
        /// <summary>Enforce usage reserves within existing eligibility and routing constraints.</summary>
        Enforce = 2,
    }
}
