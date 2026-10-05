namespace Armada.Proxy.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using Armada.Proxy.Models;
    using Armada.Proxy.Settings;

    /// <summary>
    /// Failed-login tracking for Armada.Proxy. Counts failed browser logins and tunnel handshakes per client key (the
    /// client address) within a sliding window; once the configured number of failures is reached, the key is locked out
    /// for the configured duration. A successful login clears the key. Failure counts are in memory; active lockouts are
    /// also written to a state file when one is configured, so a restart does not lift them.
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
        private readonly string? _StatePath;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Proxy settings (failure threshold, window, lockout).</param>
        /// <param name="utcNow">Optional clock.</param>
        public ProxyLoginRateLimiter(ProxySettings settings, Func<DateTime>? utcNow = null)
            : this(settings, null, utcNow)
        {
        }

        /// <summary>
        /// Instantiate with a lockout state file: unexpired lockouts in it are restored, and every new lockout is
        /// written back to it.
        /// </summary>
        /// <param name="settings">Proxy settings (failure threshold, window, lockout).</param>
        /// <param name="statePath">Path of the lockout state file, or null to keep lockouts in memory only.</param>
        /// <param name="utcNow">Optional clock.</param>
        public ProxyLoginRateLimiter(ProxySettings settings, string? statePath, Func<DateTime>? utcNow = null)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _UtcNow = utcNow ?? (() => DateTime.UtcNow);
            _StatePath = String.IsNullOrWhiteSpace(statePath) ? null : statePath;
            LoadState();
        }

        /// <summary>
        /// Default lockout state file for a proxy data directory.
        /// </summary>
        /// <param name="dataDirectory">Proxy data directory.</param>
        /// <returns>Path of proxy-lockouts.json in the directory.</returns>
        public static string DefaultStatePath(string dataDirectory)
        {
            if (String.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentNullException(nameof(dataDirectory));
            return Path.Combine(dataDirectory, "proxy-lockouts.json");
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
                    SaveStateLocked();
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

        private void LoadState()
        {
            if (_StatePath == null || !File.Exists(_StatePath)) return;
            try
            {
                ProxyLockoutState? state = JsonSerializer.Deserialize<ProxyLockoutState>(File.ReadAllText(_StatePath));
                if (state?.Lockouts == null) return;
                DateTime nowUtc = _UtcNow();
                lock (_Lock)
                {
                    foreach (ProxyLockoutEntry entry in state.Lockouts)
                    {
                        if (String.IsNullOrWhiteSpace(entry.Key)) continue;
                        DateTime until = DateTime.SpecifyKind(entry.LockedUntilUtc, DateTimeKind.Utc);
                        if (until > nowUtc) _LockedUntil[Normalize(entry.Key)] = until;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                // A missing or unreadable state file only loses lockouts that would have survived the restart.
            }
        }

        private void SaveStateLocked()
        {
            if (_StatePath == null) return;
            try
            {
                DateTime nowUtc = _UtcNow();
                ProxyLockoutState state = new ProxyLockoutState();
                foreach (KeyValuePair<string, DateTime> kv in _LockedUntil.Where(kv => kv.Value > nowUtc))
                    state.Lockouts.Add(new ProxyLockoutEntry { Key = kv.Key, LockedUntilUtc = kv.Value });

                string? directory = Path.GetDirectoryName(_StatePath);
                if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                string temp = _StatePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(state));
                File.Move(temp, _StatePath, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Persistence is best effort: the in-memory lockout still applies.
            }
        }

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
