namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>Conservative reservation lifecycle, independent of execution slot ownership.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UsageReservationStateEnum
    {
        /// <summary>Potentially executing work; remains counted through fresh captures.</summary>
        Active = 0,
        /// <summary>Execution ended; estimated consumption awaits an authoritative capture.</summary>
        Unsettled = 1,
        /// <summary>A sufficiently recent capture absorbed the estimate.</summary>
        Settled = 2,
        /// <summary>Released before any execution could consume allowance.</summary>
        Released = 3,
        /// <summary>Unsettled evidence aged beyond its configured horizon.</summary>
        Uncertain = 4
    }
}
