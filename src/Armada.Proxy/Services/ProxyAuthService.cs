namespace Armada.Proxy.Services
{
    using System.Collections.Concurrent;
    using Armada.Core;
    using Armada.Proxy.Settings;

    /// <summary>
    /// Browser-auth service for Armada.Proxy.
    /// </summary>
    public class ProxyAuthService
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ProxyAuthService(ProxySettings settings, Func<DateTime>? utcNow = null)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _UtcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Issue a one-time browser login challenge.
        /// </summary>
        public (string Nonce, DateTime ExpiresUtc) CreateChallenge()
        {
            CleanupExpired();
            string nonce = RemoteTunnelAuth.CreateNonce();
            DateTime expiresUtc = _UtcNow().AddSeconds(Math.Max(30, _Settings.HandshakeTimeoutSeconds));
            _Challenges[nonce] = expiresUtc;
            return (nonce, expiresUtc);
        }

        /// <summary>
        /// Attempt to create an authenticated browser session.
        /// </summary>
        public bool TryLogin(string? nonce, string? proofSha256, out string? sessionToken, out DateTime? expiresUtc, out string? error)
        {
            sessionToken = null;
            expiresUtc = null;
            error = null;

            CleanupExpired();

            string normalizedNonce = (nonce ?? String.Empty).Trim().ToLowerInvariant();
            string normalizedProof = (proofSha256 ?? String.Empty).Trim().ToLowerInvariant();
            if (String.IsNullOrWhiteSpace(normalizedNonce) || String.IsNullOrWhiteSpace(normalizedProof))
            {
                error = "Nonce and proof are required.";
                return false;
            }

            if (!_Challenges.TryRemove(normalizedNonce, out DateTime challengeExpiresUtc))
            {
                error = "Login challenge is missing or already used.";
                return false;
            }

            if (challengeExpiresUtc <= _UtcNow())
            {
                error = "Login challenge has expired.";
                return false;
            }

            string expectedProof = RemoteTunnelAuth.ComputeBrowserLoginProof(_Settings.Password, normalizedNonce);
            if (!RemoteTunnelAuth.FixedTimeEqualsHex(normalizedProof, expectedProof))
            {
                error = "Proxy password is invalid.";
                return false;
            }

            sessionToken = RemoteTunnelAuth.CreateNonce(24);
            expiresUtc = _UtcNow().AddHours(Constants.SessionTokenLifetimeHours);
            _Sessions[sessionToken] = expiresUtc.Value;
            return true;
        }

        /// <summary>
        /// Validate a browser session token.
        /// </summary>
        public bool TryValidateSession(string? sessionToken, out DateTime? expiresUtc)
        {
            expiresUtc = null;
            CleanupExpired();

            string normalizedToken = (sessionToken ?? String.Empty).Trim();
            if (String.IsNullOrWhiteSpace(normalizedToken))
            {
                return false;
            }

            if (!_Sessions.TryGetValue(normalizedToken, out DateTime sessionExpiresUtc))
            {
                return false;
            }

            if (sessionExpiresUtc <= _UtcNow())
            {
                _Sessions.TryRemove(normalizedToken, out DateTime _);
                return false;
            }

            expiresUtc = sessionExpiresUtc;
            return true;
        }

        /// <summary>
        /// Invalidate a browser session token.
        /// </summary>
        public void Logout(string? sessionToken)
        {
            if (String.IsNullOrWhiteSpace(sessionToken))
            {
                return;
            }

            _Sessions.TryRemove(sessionToken.Trim(), out DateTime _);
        }

        #endregion

        #region Private-Members

        private readonly ProxySettings _Settings;
        private readonly Func<DateTime> _UtcNow;
        private readonly ConcurrentDictionary<string, DateTime> _Challenges = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, DateTime> _Sessions = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);

        #endregion

        #region Private-Methods

        private void CleanupExpired()
        {
            DateTime nowUtc = _UtcNow();

            foreach (KeyValuePair<string, DateTime> challenge in _Challenges.ToArray())
            {
                if (challenge.Value <= nowUtc)
                {
                    _Challenges.TryRemove(challenge.Key, out DateTime _);
                }
            }

            foreach (KeyValuePair<string, DateTime> session in _Sessions.ToArray())
            {
                if (session.Value <= nowUtc)
                {
                    _Sessions.TryRemove(session.Key, out DateTime _);
                }
            }
        }

        #endregion
    }
}
