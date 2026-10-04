namespace Armada.Server.WebSocket
{
    /// <summary>
    /// Envelope fields common to every client-to-server /ws message: the route, plus the subscribe options.
    /// </summary>
    public class WebSocketRouteMessage
    {
        #region Public-Members

        /// <summary>
        /// Message route: <c>subscribe</c> or <c>command</c>.
        /// </summary>
        public string? Route { get; set; } = null;

        /// <summary>
        /// Subscribe option: when true and the client is a global admin, deliver entity events of every tenant.
        /// Ignored for everyone else. Default false.
        /// </summary>
        public bool AllTenants { get; set; } = false;

        #endregion
    }
}
