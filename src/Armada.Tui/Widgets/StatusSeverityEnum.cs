namespace Armada.Tui.Widgets
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Display severity of a status value (drives the badge color and ASCII marker).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum StatusSeverityEnum
    {
        /// <summary>
        /// Neutral or unknown.
        /// </summary>
        Info = 0,

        /// <summary>
        /// Finished well.
        /// </summary>
        Success = 1,

        /// <summary>
        /// Needs attention but is not a failure.
        /// </summary>
        Warning = 2,

        /// <summary>
        /// Failed.
        /// </summary>
        Error = 3,

        /// <summary>
        /// In progress.
        /// </summary>
        Running = 4
    }
}
