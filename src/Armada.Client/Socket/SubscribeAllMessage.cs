namespace Armada.Client.Socket
{
    using System;

    /// <summary>
    /// The all-tenants subscribe command sent by global admins.
    /// </summary>
    public class SubscribeAllMessage
    {
        #region Public-Members

        /// <summary>
        /// Route name.
        /// </summary>
        public string Route { get; set; } = "subscribe";

        /// <summary>
        /// Receive every tenant's events.
        /// </summary>
        public bool AllTenants { get; set; } = true;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SubscribeAllMessage()
        {
        }

        #endregion
    }
}
