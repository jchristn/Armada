namespace Armada.Proxy.Services
{
    using System.Collections.Concurrent;
    using System.Security.Cryptography;
    using System.Text;
    using Armada.Core;
    using Armada.Proxy.Settings;

    /// <summary>
    /// Browser-auth service for Armada.Proxy.
    /// </summary>
    public class ProxyAuthService
    {
        /// <summary>
        /// Authenticated browser session state.
        /// </summary>
        public sealed class ProxyBrowserSession
        {
            /// <summary>
            /// Stable opaque session token.
            /// </summary>
            public string Token { get; set; } = String.Empty;

            /// <summary>
            /// UTC expiration timestamp for the session.
            /// </summary>
            public DateTime ExpiresUtc { get; set; }

            /// <summary>
            /// Selected Armada instance for this browser session, if any.
            /// </summary>
            public string? SelectedInstanceId { get; set; } = null;
        }

        /// <summary>
        /// One-time browser login challenge metadata.
        /// </summary>
        public sealed class ProxyAuthChallenge
        {
            /// <summary>
            /// Randomized nonce to prove challenge ownership.
            /// </summary>
            public string Nonce { get; set; } = String.Empty;

            /// <summary>
            /// UTC expiration timestamp for the challenge.
            /// </summary>
            public DateTime ExpiresUtc { get; set; }
        }

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
        public ProxyAuthChallenge CreateChallenge()
        {
            CleanupExpired();
            string nonce = RemoteTunnelAuth.CreateNonce();
            DateTime expiresUtc = _UtcNow().AddSeconds(Math.Max(30, _Settings.HandshakeTimeoutSeconds));
            _Challenges[nonce] = expiresUtc;
            return new ProxyAuthChallenge
            {
                Nonce = nonce,
                ExpiresUtc = expiresUtc
            };
        }

        /// <summary>
        /// Attempt to create an authenticated browser session.
        /// </summary>
        public bool TryLogin(string? nonce, string? proofSha256, out ProxyBrowserSession? session, out string? error)
        {
            session = null;
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

            session = new ProxyBrowserSession
            {
                Token = RemoteTunnelAuth.CreateNonce(24),
                ExpiresUtc = _UtcNow().AddHours(Constants.SessionTokenLifetimeHours)
            };
            _Sessions[HashToken(session.Token)] = session;
            return true;
        }

        /// <summary>
        /// Validate a browser session token.
        /// </summary>
        public bool TryValidateSession(string? sessionToken, out DateTime? expiresUtc)
        {
            expiresUtc = null;
            if (!TryGetSession(sessionToken, out ProxyBrowserSession? session))
            {
                return false;
            }

            expiresUtc = session!.ExpiresUtc;
            return true;
        }

        /// <summary>
        /// Retrieve the current authenticated browser session.
        /// </summary>
        public bool TryGetSession(string? sessionToken, out ProxyBrowserSession? session)
        {
            session = null;
            CleanupExpired();

            string normalizedToken = (sessionToken ?? String.Empty).Trim();
            if (String.IsNullOrWhiteSpace(normalizedToken))
            {
                return false;
            }

            string key = HashToken(normalizedToken);
            if (!_Sessions.TryGetValue(key, out ProxyBrowserSession? existingSession))
            {
                return false;
            }

            if (existingSession.ExpiresUtc <= _UtcNow())
            {
                _Sessions.TryRemove(key, out ProxyBrowserSession? _);
                return false;
            }

            session = Clone(existingSession);
            return true;
        }

        /// <summary>
        /// Set or replace the selected instance for an authenticated browser session.
        /// </summary>
        public bool TrySetSelectedInstance(string? sessionToken, string? instanceId, out ProxyBrowserSession? session, out string? error)
        {
            session = null;
            error = null;

            if (!TryGetSessionForUpdate(sessionToken, out string normalizedToken, out ProxyBrowserSession? existingSession, out error))
            {
                return false;
            }

            string? normalizedInstanceId = String.IsNullOrWhiteSpace(instanceId) ? null : instanceId.Trim();
            ProxyBrowserSession updated = Clone(existingSession!);
            updated.SelectedInstanceId = normalizedInstanceId;
            _Sessions[HashToken(normalizedToken)] = updated;
            session = Clone(updated);
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

            _Sessions.TryRemove(HashToken(sessionToken.Trim()), out ProxyBrowserSession? _);
        }

        #endregion

        #region Private-Members

        private readonly ProxySettings _Settings;
        private readonly Func<DateTime> _UtcNow;
        private readonly ConcurrentDictionary<string, DateTime> _Challenges = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        // Keyed by the SHA-256 of the token, so a lookup compares digests of the presented value rather than the raw
        // secret (no timing signal about a real token's prefix) and the raw tokens are not held as dictionary keys.
        private readonly ConcurrentDictionary<string, ProxyBrowserSession> _Sessions = new ConcurrentDictionary<string, ProxyBrowserSession>(StringComparer.Ordinal);

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

            foreach (KeyValuePair<string, ProxyBrowserSession> session in _Sessions.ToArray())
            {
                if (session.Value.ExpiresUtc <= nowUtc)
                {
                    _Sessions.TryRemove(session.Key, out ProxyBrowserSession? _);
                }
            }
        }

        private bool TryGetSessionForUpdate(string? sessionToken, out string normalizedToken, out ProxyBrowserSession? session, out string? error)
        {
            error = null;
            session = null;
            normalizedToken = (sessionToken ?? String.Empty).Trim();

            if (String.IsNullOrWhiteSpace(normalizedToken))
            {
                error = "Proxy session is missing.";
                return false;
            }

            if (!_Sessions.TryGetValue(HashToken(normalizedToken), out ProxyBrowserSession? existingSession))
            {
                error = "Proxy session is invalid or expired.";
                return false;
            }

            if (existingSession.ExpiresUtc <= _UtcNow())
            {
                _Sessions.TryRemove(HashToken(normalizedToken), out ProxyBrowserSession? _);
                error = "Proxy session is invalid or expired.";
                return false;
            }

            session = existingSession;
            return true;
        }

        private static string HashToken(string token)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        }

        private static ProxyBrowserSession Clone(ProxyBrowserSession source)
        {
            return new ProxyBrowserSession
            {
                Token = source.Token,
                ExpiresUtc = source.ExpiresUtc,
                SelectedInstanceId = source.SelectedInstanceId
            };
        }

        #endregion
    }
}
