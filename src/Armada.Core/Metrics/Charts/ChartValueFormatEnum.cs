namespace Armada.Core.Metrics.Charts
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// How a chart writes its values (axis labels, tooltips, and accessible summaries).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChartValueFormatEnum
    {
        /// <summary>
        /// Whole numbers (jobs).
        /// </summary>
        [EnumMember(Value = "Count")]
        Count,

        /// <summary>
        /// Numbers with up to two decimals (average concurrent jobs).
        /// </summary>
        [EnumMember(Value = "Decimal")]
        Decimal,

        /// <summary>
        /// Milliseconds written as a compact duration (850ms, 4.2s, 3m 12s).
        /// </summary>
        [EnumMember(Value = "DurationMs")]
        DurationMs,

        /// <summary>
        /// Token counts written compactly (950, 1.5K, 2.3M).
        /// </summary>
        [EnumMember(Value = "Tokens")]
        Tokens
    }
}
