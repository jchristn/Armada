namespace Armada.Harbor
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// State of Harbor's link to the Admiral, as the window, tray, and menus show it.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborLinkStateEnum
    {
        /// <summary>
        /// Not connecting: never started, or disconnected by the operator.
        /// </summary>
        Idle,

        /// <summary>
        /// Dialing the Admiral or waiting for the handshake.
        /// </summary>
        Connecting,

        /// <summary>
        /// Linked and accepting work.
        /// </summary>
        Connected,

        /// <summary>
        /// The link dropped or could not be made; the loop retries on its own.
        /// </summary>
        Disconnected
    }
}
