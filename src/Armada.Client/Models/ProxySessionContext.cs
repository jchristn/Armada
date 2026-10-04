namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Session context when connected through Armada.Proxy.
    /// </summary>
    public class ProxySessionContext
    {
        #region Public-Members

        /// <summary>
        /// True when the proxy session is authenticated.
        /// </summary>
        public bool? IsAuthenticated { get; set; } = null;

        /// <summary>
        /// Session expiry, or null.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Selected instance id, or null.
        /// </summary>
        public string? SelectedInstanceId { get; set; } = null;

        /// <summary>
        /// Selected instance, or null.
        /// </summary>
        public ProxySelectedInstance? SelectedInstance { get; set; } = null;

        /// <summary>
        /// Relay flags, or null.
        /// </summary>
        public ProxyRelay? Relay { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ProxySessionContext()
        {
        }

        #endregion
    }
}
