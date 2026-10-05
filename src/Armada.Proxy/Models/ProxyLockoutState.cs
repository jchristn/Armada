namespace Armada.Proxy.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Persisted proxy login lockouts (proxy-lockouts.json in the proxy data directory), so a restart does not lift an
    /// active lockout.
    /// </summary>
    public class ProxyLockoutState
    {
        #region Public-Members

        /// <summary>
        /// Active lockouts.
        /// </summary>
        public List<ProxyLockoutEntry> Lockouts { get; set; } = new List<ProxyLockoutEntry>();

        #endregion
    }
}
