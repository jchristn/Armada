namespace Armada.Core.Hosting
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The key of a Harbor menu shortcut, pressed with Command on macOS and Control elsewhere.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborMenuKeyEnum
    {
        /// <summary>
        /// No shortcut.
        /// </summary>
        None,

        /// <summary>
        /// Comma (Settings).
        /// </summary>
        Comma,

        /// <summary>
        /// D (Open Dashboard).
        /// </summary>
        D,

        /// <summary>
        /// I (Status).
        /// </summary>
        I,

        /// <summary>
        /// L (Logs).
        /// </summary>
        L,

        /// <summary>
        /// M (Minimize).
        /// </summary>
        M,

        /// <summary>
        /// Q (Quit).
        /// </summary>
        Q,

        /// <summary>
        /// R (Reconnect).
        /// </summary>
        R,

        /// <summary>
        /// W (Close).
        /// </summary>
        W
    }
}
