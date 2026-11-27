namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A model alert threshold rule (docs/MODEL_HETEROGENEITY.md, Alerts).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ModelAlertRuleEnum
    {
        /// <summary>
        /// First-pass success below the threshold over at least the minimum number of missions.
        /// </summary>
        [EnumMember(Value = "FirstPassSuccess")]
        FirstPassSuccess,

        /// <summary>
        /// The most recent attempts were escalated away from the profile at least the threshold number of times in a row.
        /// </summary>
        [EnumMember(Value = "EscalationsInARow")]
        EscalationsInARow,

        /// <summary>
        /// Protocol repair rate above the threshold over at least the minimum number of attempts.
        /// </summary>
        [EnumMember(Value = "ProtocolRepairRate")]
        ProtocolRepairRate,

        /// <summary>
        /// Tokens per accepted result above the multiple of the persona median across other profiles.
        /// </summary>
        [EnumMember(Value = "CostPerAcceptedResult")]
        CostPerAcceptedResult,

        /// <summary>
        /// The profile's outcome data suggests a different tier than the one routing uses (RS-8.5). Raised only when
        /// RoutingData.SuggestTiers is on; nothing changes until an administrator applies the suggestion.
        /// </summary>
        [EnumMember(Value = "TierSuggestion")]
        TierSuggestion
    }
}
