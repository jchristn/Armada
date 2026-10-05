namespace Test.Shared.Suites.Tui.Bodies
{
    /// <summary>
    /// Route of a text frame the socket client sent (nullable: the client's own message class defaults Route to subscribe).
    /// </summary>
    public class SocketRouteFrame
    {
        #region Public-Members

        /// <summary>
        /// Route.
        /// </summary>
        public string? Route { get; set; } = null;

        #endregion
    }
}
