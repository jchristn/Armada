namespace Armada.Proxy.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Proxy.Settings;

    /// <summary>
    /// In-memory failed-login tracking for Armada.Proxy. Counts failed browser logins and tunnel handshakes per client
    /// key (the client address) within a sliding window; once the configured number of failures is reached, the key is
    /// locked out for the configured duration. A successful login clears the key.
    /// </summary>
    public class ProxyLoginRateLimiter
    {
        #region Private-Members

        private readonly ProxySettings _Settings;
        private readonly Func<DateTime> _UtcNow;
        private readonly object _Lock = new object();
        private readonly Dictionary<string, List<DateTime>> _Failures = new Dictionary<string, List<DateTime>>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _LockedUntil = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private const int _MaxTrackedKeys = 10000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Proxy settings (failure threshold, window, lockout).</param>
        /// <param name="utcNow">Optional clock.</param>
        public ProxyLoginRateLimiter(ProxySettings settings, Func<DateTime>? utcNow = null)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _UtcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the key is currently locked out.
        /// </summary>
        /// <param name="key">Client key.</param>
        /// <param name="retryAfterSeconds">Seconds until the lockout ends (0 when not locked out).</param>
        /// <returns>True when locked out.</returns>
        public bool IsLockedOut(string? key, out int retryAfterSeconds)
        {
            retryAfterSeconds = 0;
            string normalized = Normalize(key);
            lock (_Lock)
            {
                DateTime nowUtc = _UtcNow();
                if (!_LockedUntil.TryGetValue(normalized, out DateTime untilUtc)) return false;
                if (untilUtc <= nowUtc)
                {
                    _LockedUntil.Remove(normalized);
                    return false;
                }

                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((untilUtc - nowUtc).TotalSeconds));
                return true;
            }
        }

        /// <summary>
        /// Record a failed attempt. Returns true when this failure starts a lockout.
        /// </summary>
        /// <param name="key">Client key.</param>
        /// <param name="retryAfterSeconds">Lockout length in seconds when a lockout started, otherwise 0.</param>
        /// <returns>True when the key is now locked out.</returns>
        public bool RecordFailure(string? key, out int retryAfterSeconds)
        {
            retryAfterSeconds = 0;
            string normalized = Normalize(key);
            lock (_Lock)
            {
                DateTime nowUtc = _UtcNow();
                Prune(nowUtc);

                if (!_Failures.TryGetValue(normalized, out List<DateTime>? failures))
                {
                    failures = new List<DateTime>();
                    _Failures[normalized] = failures;
                }

                DateTime windowStartUtc = nowUtc.AddSeconds(-_Settings.LoginFailureWindowSeconds);
                failures.RemoveAll(f => f < windowStartUtc);
                failures.Add(nowUtc);

                if (failures.Count >= _Settings.LoginMaxFailures)
                {
                    _LockedUntil[normalized] = nowUtc.AddSeconds(_Settings.LoginLockoutSeconds);
                    _Failures.Remove(normalized);
                    retryAfterSeconds = _Settings.LoginLockoutSeconds;
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Clear the failure history of a key after a successful login.
        /// </summary>
        /// <param name="key">Client key.</param>
        public void RecordSuccess(string? key)
        {
            string normalized = Normalize(key);
            lock (_Lock)
            {
                _Failures.Remove(normalized);
            }
        }

        #endregion

        #region Private-Methods

        private static string Normalize(string? key)
        {
            return String.IsNullOrWhiteSpace(key) ? "unknown" : key.Trim().ToLowerInvariant();
        }

        private void Prune(DateTime nowUtc)
        {
            foreach (string expired in _LockedUntil.Where(kv => kv.Value <= nowUtc).Select(kv => kv.Key).ToList())
            {
                _LockedUntil.Remove(expired);
            }

            if (_Failures.Count < _MaxTrackedKeys) return;

            DateTime windowStartUtc = nowUtc.AddSeconds(-_Settings.LoginFailureWindowSeconds);
            foreach (string stale in _Failures.Where(kv => kv.Value.Count == 0 || kv.Value.Max() < windowStartUtc).Select(kv => kv.Key).ToList())
            {
                _Failures.Remove(stale);
            }
        }

        #endregion
    }
}
