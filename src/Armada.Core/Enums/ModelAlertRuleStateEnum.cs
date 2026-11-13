namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The result of one alert rule for one profile, revision group, persona and vessel.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ModelAlertRuleStateEnum
    {
        /// <summary>
        /// The threshold is crossed and the minimum count is met.
        /// </summary>
        [EnumMember(Value = "Crossed")]
        Crossed,

        /// <summary>
        /// The minimum count is met and the threshold is not crossed.
        /// </summary>
        [EnumMember(Value = "Clear")]
        Clear,

        /// <summary>
        /// Too few missions, attempts or accepted results to judge.
        /// </summary>
        [EnumMember(Value = "MinimumNotMet")]
        MinimumNotMet,

        /// <summary>
        /// The scorecard does not provide the metric (the rule is inactive), with the reason.
        /// </summary>
        [EnumMember(Value = "Unavailable")]
        Unavailable
    }
}
