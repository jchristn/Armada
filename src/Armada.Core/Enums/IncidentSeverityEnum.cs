namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Incident severity levels.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum IncidentSeverityEnum
    {
        /// <summary>
        /// Critical service-impacting incident.
        /// </summary>
        Critical,

        /// <summary>
        /// High-priority incident.
        /// </summary>
        High,

        /// <summary>
        /// Medium-priority incident.
        /// </summary>
        Medium,

        /// <summary>
        /// Low-priority incident.
        /// </summary>
        Low
    }
}
