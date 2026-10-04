namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// In-memory login rate limiter with lockout and exponential backoff (see <see cref="LoginRateLimitSettings"/>).
    /// Keys are accounts (tenant and email) for password logins, client addresses for every failed authentication, and a
    /// separate per-address budget for tenant lookup and onboarding requests. Thread-safe. State is lost on restart.
    /// </summary>
    public class LoginRateLimiter
    {
        #region Private-Members

        private const int _PruneThreshold = 10000;
        private readonly LoginRateLimitSettings _Settings;
        private readonly Func<DateTime> _Clock;
        private readonly Dictionary<string, LoginFailureState> _States = new Dictionary<string, LoginFailureState>(StringComparer.Ordinal);
        private readonly object _Lock = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Rate limit settings (read on every call, so changes apply immediately).</param>
        /// <param name="clock">Optional UTC clock (tests).</param>
        public LoginRateLimiter(LoginRateLimitSettings settings, Func<DateTime>? clock = null)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Clock = clock ?? (() => DateTime.UtcNow);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Remaining lockout for a password login by this account from this address, or null when allowed.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="email">Email address.</param>
        /// <param name="address">Client address.</param>
        /// <returns>Time until the lockout ends, or null.</returns>
        public TimeSpan? CheckPasswordLogin(string? tenantId, string? email, string? address)
        {
            if (!_Settings.Enabled) return null;
            TimeSpan? account = GetRetryAfter(AccountKey(tenantId, email));
            TimeSpan? addr = String.IsNullOrEmpty(address) ? null : GetRetryAfter(AddressKey(address));
            return Max(account, addr);
        }

        /// <summary>
        /// Record a failed password login for the account and the address.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="email">Email address.</param>
        /// <param name="address">Client address.</param>
        public void RecordPasswordFailure(string? tenantId, string? email, string? address)
        {
            if (!_Settings.Enabled) return;
            RecordFailure(AccountKey(tenantId, email), _Settings.MaxFailuresPerAccount);
            if (!String.IsNullOrEmpty(address)) RecordFailure(AddressKey(address), _Settings.MaxFailuresPerAddress);
        }

        /// <summary>
        /// Clear the account's counter after a successful password login.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="email">Email address.</param>
        public void RecordPasswordSuccess(string? tenantId, string? email)
        {
            lock (_Lock)
            {
                _States.Remove(AccountKey(tenantId, email));
            }
        }

        /// <summary>
        /// Remaining lockout for credential (bearer token, API key) authentication from this address, or null.
        /// </summary>
        /// <param name="address">Client address.</param>
        /// <returns>Time until the lockout ends, or null.</returns>
        public TimeSpan? CheckAddress(string? address)
        {
            if (!_Settings.Enabled || String.IsNullOrEmpty(address)) return null;
            return GetRetryAfter(AddressKey(address));
        }

        /// <summary>
        /// Record a failed credential authentication from this address.
        /// </summary>
        /// <param name="address">Client address.</param>
        public void RecordAddressFailure(string? address)
        {
            if (!_Settings.Enabled || String.IsNullOrEmpty(address)) return;
            RecordFailure(AddressKey(address), _Settings.MaxFailuresPerAddress);
        }

        /// <summary>
        /// Check and count one tenant lookup or onboarding request from this address against its own per-address budget.
        /// </summary>
        /// <param name="address">Client address.</param>
        /// <returns>Time until the lockout ends when the request is refused, or null when it is allowed (and counted).</returns>
        public TimeSpan? CheckAndCountLookup(string? address)
        {
            if (!_Settings.Enabled || String.IsNullOrEmpty(address)) return null;
            string key = LookupKey(address);
            TimeSpan? locked = GetRetryAfter(key);
            if (locked != null) return locked;
            RecordFailure(key, _Settings.MaxFailuresPerAddress);
            return null;
        }

        /// <summary>
        /// Retry-After header value (whole seconds, at least 1) for a remaining lockout.
        /// </summary>
        /// <param name="retryAfter">Remaining lockout.</param>
        /// <returns>Seconds as text.</returns>
        public static string ToRetryAfterSeconds(TimeSpan retryAfter)
        {
            double seconds = Math.Ceiling(retryAfter.TotalSeconds);
            if (seconds < 1) seconds = 1;
            return ((long)seconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        #endregion

        #region Private-Methods

        private static string AccountKey(string? tenantId, string? email)
        {
            return "acct:" + (tenantId ?? String.Empty) + "|" + (email ?? String.Empty).Trim().ToLowerInvariant();
        }

        private static string AddressKey(string address)
        {
            return "addr:" + address;
        }

        private static string LookupKey(string address)
        {
            return "lookup:" + address;
        }

        private static TimeSpan? Max(TimeSpan? a, TimeSpan? b)
        {
            if (a == null) return b;
            if (b == null) return a;
            return a.Value >= b.Value ? a : b;
        }

        private TimeSpan? GetRetryAfter(string key)
        {
            DateTime now = _Clock();
            lock (_Lock)
            {
                if (!_States.TryGetValue(key, out LoginFailureState? state)) return null;
                if (state.LockedUntilUtc == null || state.LockedUntilUtc.Value <= now) return null;
                return state.LockedUntilUtc.Value - now;
            }
        }

        private void RecordFailure(string key, int maxFailures)
        {
            DateTime now = _Clock();
            TimeSpan window = TimeSpan.FromMinutes(_Settings.WindowMinutes);
            int maxLockoutMinutes = Math.Max(_Settings.MaxLockoutMinutes, _Settings.LockoutMinutes);

            lock (_Lock)
            {
                if (_States.Count >= _PruneThreshold) Prune(now, maxLockoutMinutes);

                if (!_States.TryGetValue(key, out LoginFailureState? state))
                {
                    state = new LoginFailureState { WindowStartUtc = now };
                    _States[key] = state;
                }

                // A key that has been quiet (no failure, no lockout) for longer than the longest lockout starts over: its
                // backoff is forgotten.
                if (now - LastActivity(state) > TimeSpan.FromMinutes(maxLockoutMinutes))
                {
                    state.Lockouts = 0;
                    state.Failures = 0;
                    state.WindowStartUtc = now;
                }

                state.LastFailureUtc = now;
                if (state.LockedUntilUtc != null && state.LockedUntilUtc.Value > now) return;

                if (now - state.WindowStartUtc > window)
                {
                    state.Failures = 0;
                    state.WindowStartUtc = now;
                }

                state.Failures++;
                if (state.Failures < maxFailures) return;

                double minutes = _Settings.LockoutMinutes * Math.Pow(2, Math.Min(state.Lockouts, 16));
                if (minutes > maxLockoutMinutes) minutes = maxLockoutMinutes;
                state.LockedUntilUtc = now.AddMinutes(minutes);
                state.Lockouts++;
                state.Failures = 0;
                state.WindowStartUtc = now;
            }
        }

        private static DateTime LastActivity(LoginFailureState state)
        {
            if (state.LockedUntilUtc != null && state.LockedUntilUtc.Value > state.LastFailureUtc) return state.LockedUntilUtc.Value;
            return state.LastFailureUtc;
        }

        private void Prune(DateTime now, int maxLockoutMinutes)
        {
            List<string> stale = new List<string>();
            foreach (KeyValuePair<string, LoginFailureState> entry in _States)
            {
                if (now - LastActivity(entry.Value) > TimeSpan.FromMinutes(maxLockoutMinutes)) stale.Add(entry.Key);
            }

            foreach (string key in stale) _States.Remove(key);
        }

        #endregion
    }
}
