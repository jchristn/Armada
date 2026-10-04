namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// In-memory failure counter for one rate-limited key (an account or a client address) in
    /// <see cref="Armada.Core.Services.LoginRateLimiter"/>.
    /// </summary>
    public class LoginFailureState
    {
        #region Public-Members

        /// <summary>
        /// Failures counted in the current window.
        /// </summary>
        public int Failures { get; set; } = 0;

        /// <summary>
        /// Start of the current counting window (UTC).
        /// </summary>
        public DateTime WindowStartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// End of the most recent lockout (UTC), or null when the key has not been locked out; the key is locked while this
        /// is in the future.
        /// </summary>
        public DateTime? LockedUntilUtc { get; set; } = null;

        /// <summary>
        /// Number of lockouts applied to this key since it was last cleared; each doubles the next lockout.
        /// </summary>
        public int Lockouts { get; set; } = 0;

        /// <summary>
        /// Last time a failure was recorded (UTC); stale entries are pruned.
        /// </summary>
        public DateTime LastFailureUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public LoginFailureState()
        {
        }

        #endregion
    }
}
