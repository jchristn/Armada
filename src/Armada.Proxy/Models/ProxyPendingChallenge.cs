namespace Armada.Proxy.Models
{
    using System;

    /// <summary>
    /// An outstanding (issued, unused) proxy login challenge: when it expires and which client address asked for it.
    /// </summary>
    public sealed class ProxyPendingChallenge
    {
        #region Public-Members

        /// <summary>
        /// UTC expiration of the challenge.
        /// </summary>
        public DateTime ExpiresUtc { get; }

        /// <summary>
        /// The requesting client's address, or an empty string when unknown.
        /// </summary>
        public string ClientKey { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="expiresUtc">UTC expiration.</param>
        /// <param name="clientKey">Client address, or an empty string.</param>
        public ProxyPendingChallenge(DateTime expiresUtc, string clientKey)
        {
            ExpiresUtc = expiresUtc;
            ClientKey = clientKey ?? String.Empty;
        }

        #endregion
    }
}
