namespace Armada.Harbor
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Tabs of the Settings window.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SettingsTabEnum
    {
        /// <summary>
        /// This computer's Harbor settings and the appearance.
        /// </summary>
        General,

        /// <summary>
        /// Where each vessel's repository is checked out on this computer.
        /// </summary>
        Repositories,

        /// <summary>
        /// The Admiral server's settings (live through the API, or its settings.json file when it is on this computer).
        /// </summary>
        AdmiralServer
    }
}
