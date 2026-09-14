namespace Armada.Publisher.Manifest
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Classifies an artifact by its runtime shape, which drives packaging and startup behavior.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ArtifactKindEnum
    {
        /// <summary>
        /// Cross-platform command-line tool (no GUI, no long-running service).
        /// </summary>
        Cli,

        /// <summary>
        /// GUI or tray application that autostarts per user.
        /// </summary>
        Tray,

        /// <summary>
        /// Long-running daemon installed as a system service.
        /// </summary>
        Service
    }
}
