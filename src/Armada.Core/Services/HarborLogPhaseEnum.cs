namespace Armada.Core.Services
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where a Harbor log entry sits in a request and result pair.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborLogPhaseEnum
    {
        /// <summary>
        /// Not part of a pair.
        /// </summary>
        [EnumMember(Value = "None")]
        None,

        /// <summary>
        /// Work received; its result has the same request ID.
        /// </summary>
        [EnumMember(Value = "Request")]
        Request,

        /// <summary>
        /// The result of a request with the same request ID.
        /// </summary>
        [EnumMember(Value = "Result")]
        Result
    }
}
