namespace Armada.Proxy.Models
{
    /// <summary>
    /// Body of POST /proxy-api/v1/session/instance.
    /// </summary>
    public class ProxySelectInstanceRequest
    {
        #region Public-Members

        /// <summary>
        /// Instance to select for the browser session.
        /// </summary>
        public string? InstanceId { get; set; } = null;

        #endregion
    }
}
