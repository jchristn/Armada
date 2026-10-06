namespace Armada.Helm.Infrastructure
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where the CLI's Admiral target came from (resolution order: flag, environment, active profile, local default).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AdmiralTargetSourceEnum
    {
        /// <summary>
        /// No flag, environment variable, or remote profile: this machine's Admiral at 127.0.0.1 and admiralPort.
        /// </summary>
        LocalDefault,

        /// <summary>
        /// The <c>--server</c> or <c>--profile</c> option.
        /// </summary>
        Flag,

        /// <summary>
        /// The <c>ARMADA_SERVER_URL</c> (or <c>ARMADA_URL</c>) environment variable.
        /// </summary>
        Environment,

        /// <summary>
        /// The active profile in the shared TUI preferences file (<c>tui.json</c>).
        /// </summary>
        Profile
    }
}
