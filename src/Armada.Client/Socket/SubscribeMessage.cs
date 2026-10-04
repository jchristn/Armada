namespace Armada.Client.Socket
{
    using System;

    /// <summary>
    /// The subscribe command sent when the socket opens.
    /// </summary>
    public class SubscribeMessage
    {
        #region Public-Members

        /// <summary>
        /// Route name.
        /// </summary>
        public string Route { get; set; } = "subscribe";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SubscribeMessage()
        {
        }

        #endregion
    }
}
