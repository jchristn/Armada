namespace Armada.Core.BlackBox
{
    using System;

    /// <summary>
    /// The black box entry is being deleted. REST 409. Code: BlackBoxDeleting.
    /// </summary>
    public class BlackBoxDeletingException : BlackBoxException
    {
        /// <summary>
        /// Error code.
        /// </summary>
        public const string ErrorCode = "BlackBoxDeleting";

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="inner">Inner exception, or null.</param>
        public BlackBoxDeletingException(string message, Exception? inner = null) : base(ErrorCode, message, inner)
        {
        }
    }
}
