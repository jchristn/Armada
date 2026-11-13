namespace Armada.Core.BlackBox
{
    using System;

    /// <summary>
    /// A black box object's bytes do not match their SHA-256. REST 409. Code: BlackBoxObjectCorrupt.
    /// </summary>
    public class BlackBoxObjectCorruptException : BlackBoxException
    {
        /// <summary>
        /// Error code.
        /// </summary>
        public const string ErrorCode = "BlackBoxObjectCorrupt";

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="inner">Inner exception, or null.</param>
        public BlackBoxObjectCorruptException(string message, Exception? inner = null) : base(ErrorCode, message, inner)
        {
        }
    }
}
