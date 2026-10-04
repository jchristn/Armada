namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// What an Armada.Proxy session relays.
    /// </summary>
    public class ProxyRelay
    {
        #region Public-Members

        /// <summary>
        /// Relays the dashboard.
        /// </summary>
        public bool? Dashboard { get; set; } = null;

        /// <summary>
        /// Relays the API.
        /// </summary>
        public bool? Api { get; set; } = null;

        /// <summary>
        /// Relays the WebSocket.
        /// </summary>
        public bool? Websocket { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ProxyRelay()
        {
        }

        #endregion
    }
}
