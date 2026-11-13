namespace Armada.Core.BlackBox
{
    using System;

    /// <summary>
    /// The black box entry (or the subject's current entry) does not exist or is not visible to the caller. REST 404. Code: BlackBoxNotFound.
    /// </summary>
    public class BlackBoxNotFoundException : BlackBoxException
    {
        /// <summary>
        /// Error code.
        /// </summary>
        public const string ErrorCode = "BlackBoxNotFound";

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="inner">Inner exception, or null.</param>
        public BlackBoxNotFoundException(string message, Exception? inner = null) : base(ErrorCode, message, inner)
        {
        }
    }
}
