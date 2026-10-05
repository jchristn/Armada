namespace Armada.Proxy.Models
{
    using System;

    /// <summary>
    /// One persisted proxy login lockout: the client key and when the lockout ends.
    /// </summary>
    public class ProxyLockoutEntry
    {
        #region Public-Members

        /// <summary>
        /// Normalized client key (the client address).
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// When the lockout ends (UTC).
        /// </summary>
        public DateTime LockedUntilUtc { get; set; } = DateTime.MinValue;

        #endregion
    }
}
