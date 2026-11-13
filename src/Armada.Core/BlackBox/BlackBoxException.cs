namespace Armada.Core.BlackBox
{
    using System;

    /// <summary>
    /// Base class of black box errors. Code is the stable error code routes map to responses (docs/BLACKBOX.md).
    /// </summary>
    public class BlackBoxException : Exception
    {
        /// <summary>
        /// Stable error code (for example BlackBoxNotFound).
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="code">Error code.</param>
        /// <param name="message">Message.</param>
        /// <param name="inner">Inner exception, or null.</param>
        public BlackBoxException(string code, string message, Exception? inner = null) : base(message, inner)
        {
            Code = String.IsNullOrEmpty(code) ? "BlackBoxError" : code;
        }
    }
}
