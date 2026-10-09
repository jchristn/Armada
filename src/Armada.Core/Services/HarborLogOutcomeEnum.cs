namespace Armada.Core.Services
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// How the operation a Harbor log entry reports turned out.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborLogOutcomeEnum
    {
        /// <summary>
        /// Not an outcome (a request, or an event).
        /// </summary>
        [EnumMember(Value = "None")]
        None,

        /// <summary>
        /// Succeeded, including a command whose non-zero exit code the caller declared an expected answer.
        /// </summary>
        [EnumMember(Value = "Ok")]
        Ok,

        /// <summary>
        /// Failed: always shown in the activity log's summary view.
        /// </summary>
        [EnumMember(Value = "Failed")]
        Failed
    }
}
