namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Severity of a "needs you" inbox item.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum InboxSeverityEnum
    {
        /// <summary>
        /// Informational; no urgent action required.
        /// </summary>
        Info = 0,

        /// <summary>
        /// Something needs attention.
        /// </summary>
        Warning = 1,

        /// <summary>
        /// Something is blocking progress and needs prompt action.
        /// </summary>
        Critical = 2
    }
}
