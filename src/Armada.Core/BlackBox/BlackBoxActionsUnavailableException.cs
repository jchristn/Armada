namespace Armada.Core.BlackBox
{
    using System;

    /// <summary>
    /// The host cannot run black box recovery actions or apply recommendations because no action executor is wired (a
    /// confirmed run on a host without one). REST 503, MCP Unavailable. Code: BlackBoxActionsUnavailable.
    /// </summary>
    public class BlackBoxActionsUnavailableException : BlackBoxException
    {
        /// <summary>
        /// Error code.
        /// </summary>
        public const string ErrorCode = "BlackBoxActionsUnavailable";

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="inner">Inner exception, or null.</param>
        public BlackBoxActionsUnavailableException(string message, Exception? inner = null) : base(ErrorCode, message, inner)
        {
        }
    }
}
