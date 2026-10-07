namespace Armada.Core.Services.Push
{
    using System;

    /// <summary>
    /// A push transport request failed.
    /// </summary>
    public class PushTransportException : Exception
    {
        #region Public-Members

        /// <summary>
        /// Whether retrying may succeed (HTTP 429 or 5xx, timeouts, connection failures).
        /// </summary>
        public bool Transient { get; }

        /// <summary>
        /// HTTP status code, when a response was received.
        /// </summary>
        public int? StatusCode { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="transient">Whether a retry may succeed.</param>
        /// <param name="statusCode">HTTP status code, or null.</param>
        /// <param name="inner">Inner exception, or null.</param>
        public PushTransportException(string message, bool transient, int? statusCode = null, Exception? inner = null)
            : base(message, inner)
        {
            Transient = transient;
            StatusCode = statusCode;
        }

        #endregion
    }
}
