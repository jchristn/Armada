namespace Armada.Core.Metrics.Charts
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// How a bucketed time chart draws its series.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BucketChartKindEnum
    {
        /// <summary>
        /// One bar per bucket, the series stacked bottom to top.
        /// </summary>
        [EnumMember(Value = "StackedBar")]
        StackedBar,

        /// <summary>
        /// One line per series through the bucket centers.
        /// </summary>
        [EnumMember(Value = "Line")]
        Line
    }
}
