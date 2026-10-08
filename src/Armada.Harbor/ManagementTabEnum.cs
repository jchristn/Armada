namespace Armada.Harbor
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Tabs of the management window.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ManagementTabEnum
    {
        /// <summary>
        /// Harbor, link, and Admiral status, and data directory usage.
        /// </summary>
        Status,

        /// <summary>
        /// This Harbor's settings.
        /// </summary>
        Harbor,

        /// <summary>
        /// The Admiral's settings (live through the API, or the settings.json file).
        /// </summary>
        Armada,

        /// <summary>
        /// The terminal UI's tui.json.
        /// </summary>
        Tui,

        /// <summary>
        /// The log browser and viewer.
        /// </summary>
        Logs,

        /// <summary>
        /// The Admiral's database backups.
        /// </summary>
        Backups
    }
}
