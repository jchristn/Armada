namespace Armada.Proxy.Services
{
    using System;
    using Armada.Proxy.Enums;

    /// <summary>
    /// Thrown by <see cref="ProxyAuthService.CreateChallenge(string?)"/> when the bounded store of outstanding login
    /// challenges is full for the client address or in total. Callers branch on <see cref="Refusal"/>, never on the
    /// message.
    /// </summary>
    public class ProxyChallengeLimitException : Exception
    {
        #region Public-Members

        /// <summary>
        /// Which limit was reached.
        /// </summary>
        public ProxyChallengeRefusalEnum Refusal { get; }

        /// <summary>
        /// Seconds until an outstanding challenge expires and frees a slot (at least 1).
        /// </summary>
        public int RetryAfterSeconds { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="refusal">Which limit was reached.</param>
        /// <param name="retryAfterSeconds">Seconds until a slot frees up.</param>
        public ProxyChallengeLimitException(ProxyChallengeRefusalEnum refusal, int retryAfterSeconds)
            : base(refusal == ProxyChallengeRefusalEnum.AddressLimit
                ? "Too many sign-in challenges from this address. Try again shortly."
                : "The proxy is handling too many sign-in attempts. Try again shortly.")
        {
            Refusal = refusal;
            RetryAfterSeconds = Math.Max(1, retryAfterSeconds);
        }

        #endregion
    }
}
