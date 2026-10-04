namespace Armada.Core.Settings
{
    /// <summary>
    /// Login rate limiting and lockout settings. Failed password logins are counted per account (tenant and email) and per
    /// client address; failed bearer token and API key authentications, tenant lookups, and onboarding requests are counted
    /// per client address. When a counter reaches its limit inside the window the key is locked out: requests get 429 with
    /// a Retry-After header, even with the correct password. Each further lockout of the same key doubles the lockout, up to
    /// <see cref="MaxLockoutMinutes"/>. A successful password login clears the account counter. Counters are kept in
    /// memory and reset when the Admiral restarts.
    /// </summary>
    public class LoginRateLimitSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether login rate limiting is enabled (default true).
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Failed password logins for one account (tenant and email) inside the window that trigger a lockout
        /// (default 10, 1 to 1000).
        /// </summary>
        public int MaxFailuresPerAccount
        {
            get => _MaxFailuresPerAccount;
            set => _MaxFailuresPerAccount = Clamp(value, 1, 1000);
        }

        /// <summary>
        /// Failed authentications (password, bearer token, API key) or tenant lookup and onboarding requests from one client
        /// address inside the window that trigger a lockout of that address (default 50, 1 to 100000). Higher than the
        /// per-account limit because several users can share an address.
        /// </summary>
        public int MaxFailuresPerAddress
        {
            get => _MaxFailuresPerAddress;
            set => _MaxFailuresPerAddress = Clamp(value, 1, 100000);
        }

        /// <summary>
        /// Window in minutes over which failures are counted (default 15, 1 to 1440).
        /// </summary>
        public int WindowMinutes
        {
            get => _WindowMinutes;
            set => _WindowMinutes = Clamp(value, 1, 1440);
        }

        /// <summary>
        /// Duration in minutes of the first lockout (default 15, 1 to 1440). Each further lockout of the same key doubles it.
        /// </summary>
        public int LockoutMinutes
        {
            get => _LockoutMinutes;
            set => _LockoutMinutes = Clamp(value, 1, 1440);
        }

        /// <summary>
        /// Upper bound in minutes of a doubled lockout (default 1440, 1 to 10080). Never less than <see cref="LockoutMinutes"/>.
        /// </summary>
        public int MaxLockoutMinutes
        {
            get => _MaxLockoutMinutes;
            set => _MaxLockoutMinutes = Clamp(value, 1, 10080);
        }

        #endregion

        #region Private-Members

        private int _MaxFailuresPerAccount = 10;
        private int _MaxFailuresPerAddress = 50;
        private int _WindowMinutes = 15;
        private int _LockoutMinutes = 15;
        private int _MaxLockoutMinutes = 1440;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public LoginRateLimitSettings()
        {
        }

        #endregion

        #region Private-Methods

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        #endregion
    }
}
