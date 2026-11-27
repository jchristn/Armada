namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Result of one gate measure.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GateMeasureStatusEnum
    {
        /// <summary>Measured and within its threshold.</summary>
        Pass,

        /// <summary>Measured and outside its threshold.</summary>
        Fail,

        /// <summary>Not measured by this run; <see cref="Armada.Core.Models.GateMeasure.Detail"/> says where it is measured or what is missing.</summary>
        NotMeasured
    }
}
