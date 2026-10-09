namespace Armada.Core.Services
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// How prominent a Harbor log entry is in the Harbor app's activity log.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborLogLevelEnum
    {
        /// <summary>
        /// Shown in the summary view (the default): connects, handshakes, jobs, docks, check runs, and failures.
        /// </summary>
        [EnumMember(Value = "Summary")]
        Summary,

        /// <summary>
        /// Shown only in the detail view: routine git and file work, and the requests behind a result.
        /// </summary>
        [EnumMember(Value = "Detail")]
        Detail
    }
}
