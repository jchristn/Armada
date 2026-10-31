namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// How thorough an Evaluate is.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ModelProbeDepthEnum
    {
        /// <summary>
        /// Mechanical probes only (the default).
        /// </summary>
        [EnumMember(Value = "Quick")]
        Quick,

        /// <summary>
        /// Adds the fixture battery, the evaluator's grading and an advisory model profile revision (only when asked for).
        /// </summary>
        [EnumMember(Value = "Full")]
        Full
    }
}
