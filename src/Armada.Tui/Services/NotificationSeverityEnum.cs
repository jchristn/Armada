namespace Armada.Tui.Services
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Notification severity (the dashboard's info, success, warning, error).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum NotificationSeverityEnum
    {
        /// <summary>
        /// Informational.
        /// </summary>
        Info = 0,

        /// <summary>
        /// Success.
        /// </summary>
        Success = 1,

        /// <summary>
        /// Warning.
        /// </summary>
        Warning = 2,

        /// <summary>
        /// Error.
        /// </summary>
        Error = 3
    }
}
