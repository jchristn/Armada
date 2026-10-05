namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Severity levels for vessel-readiness issues.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ReadinessSeverityEnum
    {
        /// <summary>
        /// Informational item.
        /// </summary>
        Info = 0,

        /// <summary>
        /// Warning that should be reviewed but does not necessarily block execution.
        /// </summary>
        Warning = 1,

        /// <summary>
        /// Blocking error that prevents the requested action.
        /// </summary>
        Error = 2
    }
}
