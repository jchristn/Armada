namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Window of a Harbor metrics request. On the wire (the <c>range</c> query parameter and the response) it is written
    /// 1h, 24h, or 7d; see <see cref="Armada.Core.Metrics.HarborMetricsRanges"/>.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborMetricsRangeEnum
    {
        /// <summary>
        /// The last hour, in 1-minute buckets.
        /// </summary>
        [EnumMember(Value = "OneHour")]
        OneHour,

        /// <summary>
        /// The last 24 hours, in 30-minute buckets.
        /// </summary>
        [EnumMember(Value = "OneDay")]
        OneDay,

        /// <summary>
        /// The last 7 days, in 3-hour buckets.
        /// </summary>
        [EnumMember(Value = "SevenDays")]
        SevenDays
    }
}
