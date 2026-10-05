namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Incident lifecycle states.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum IncidentStatusEnum
    {
        /// <summary>
        /// Newly opened incident that is still active.
        /// </summary>
        Open,

        /// <summary>
        /// Incident is being actively monitored after mitigation work.
        /// </summary>
        Monitoring,

        /// <summary>
        /// Incident impact has been mitigated but follow-up may remain.
        /// </summary>
        Mitigated,

        /// <summary>
        /// Incident was resolved via rollback.
        /// </summary>
        RolledBack,

        /// <summary>
        /// Incident has been fully closed.
        /// </summary>
        Closed
    }
}
