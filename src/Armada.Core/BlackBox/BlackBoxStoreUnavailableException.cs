namespace Armada.Core.BlackBox
{
    using System;

    /// <summary>
    /// A black box storage target is unavailable or not implemented. REST 503. Code: BlackBoxStoreUnavailable.
    /// </summary>
    public class BlackBoxStoreUnavailableException : BlackBoxException
    {
        /// <summary>
        /// Error code.
        /// </summary>
        public const string ErrorCode = "BlackBoxStoreUnavailable";

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="inner">Inner exception, or null.</param>
        public BlackBoxStoreUnavailableException(string message, Exception? inner = null) : base(ErrorCode, message, inner)
        {
        }
    }
}
