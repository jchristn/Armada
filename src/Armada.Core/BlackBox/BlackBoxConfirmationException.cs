namespace Armada.Core.BlackBox
{
    using System;

    /// <summary>
    /// A recovery or recommendation run was refused: no dry run, an expired or mismatched confirm token, a changed mission, or a data-loss path without acknowledgeDataLoss. REST 409. Code: BlackBoxConfirmationRequired.
    /// </summary>
    public class BlackBoxConfirmationException : BlackBoxException
    {
        /// <summary>
        /// Error code.
        /// </summary>
        public const string ErrorCode = "BlackBoxConfirmationRequired";

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="inner">Inner exception, or null.</param>
        public BlackBoxConfirmationException(string message, Exception? inner = null) : base(ErrorCode, message, inner)
        {
        }
    }
}
