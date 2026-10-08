namespace Armada.Harbor
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Tabs of the Status window.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum StatusTabEnum
    {
        /// <summary>
        /// This Harbor and its link, the Admiral's health and workload, and data directory disk usage.
        /// </summary>
        Overview,

        /// <summary>
        /// This Harbor's charts from the Admiral: jobs over time, slot usage, link health and round trip, launch speed,
        /// and token usage, over the last hour, 24 hours, or 7 days.
        /// </summary>
        Activity,

        /// <summary>
        /// Harbor's log, the logs of jobs run on this computer, and the Admiral's logs when it is on this computer.
        /// </summary>
        Logs
    }
}
