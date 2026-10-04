namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Graded status of a vessel health criterion or of a vessel overall. Unknown and NotApplicable never make a vessel look healthy, and never fail it either.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VesselHealthStatusEnum
    {
        /// <summary>
        /// The criterion is healthy.
        /// </summary>
        Pass,

        /// <summary>
        /// The criterion needs attention.
        /// </summary>
        Warn,

        /// <summary>
        /// The criterion is unhealthy.
        /// </summary>
        Fail,

        /// <summary>
        /// The criterion does not apply to this vessel (for example, no working directory).
        /// </summary>
        NotApplicable,

        /// <summary>
        /// The criterion could not be evaluated, or has not been evaluated yet.
        /// </summary>
        Unknown
    }
}
