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
        /// Harbor's log, the logs of jobs run on this computer, and the Admiral's logs when it is on this computer.
        /// </summary>
        Logs
    }
}
