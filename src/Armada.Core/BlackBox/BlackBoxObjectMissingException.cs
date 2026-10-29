namespace Armada.Core.BlackBox
{
    using System;

    /// <summary>
    /// A black box object's bytes are missing from its store. REST 410. Code: BlackBoxObjectMissing.
    /// </summary>
    public class BlackBoxObjectMissingException : BlackBoxException
    {
        /// <summary>
        /// Error code.
        /// </summary>
        public const string ErrorCode = "BlackBoxObjectMissing";

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="inner">Inner exception, or null.</param>
        public BlackBoxObjectMissingException(string message, Exception? inner = null) : base(ErrorCode, message, inner)
        {
        }
    }
}
