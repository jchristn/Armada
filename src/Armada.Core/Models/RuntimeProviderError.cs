namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// A structured provider error reported by a captain runtime: the HTTP status and the provider's own machine
    /// error type, as carried by the runtime's error event or HTTP response. Built only from structured sources
    /// (an HTTP response, a deserialized provider error payload, or a runtime's protocol error line); never from a
    /// keyword search over free-form output.
    /// </summary>
    public class RuntimeProviderError
    {
        #region Public-Members

        /// <summary>
        /// HTTP status code returned by the provider, when known (for example 429, 401, 403, 404, 529).
        /// </summary>
        public int? HttpStatusCode { get; set; } = null;

        /// <summary>
        /// The provider's machine-readable error type, when known (for example "rate_limit_error",
        /// "authentication_error", "insufficient_quota").
        /// </summary>
        public string? ErrorType { get; set; } = null;

        /// <summary>
        /// The provider's human-readable error message, for display only.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Seconds until the provider allows another request, when the provider stated it (for example a
        /// Retry-After value).
        /// </summary>
        public int? RetryAfterSeconds { get; set; } = null;

        /// <summary>
        /// Absolute UTC time at which the provider's limit resets, when the provider stated it.
        /// </summary>
        public DateTime? ResetUtc { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// A short human-readable description for logs and failure reasons.
        /// </summary>
        /// <returns>Description text.</returns>
        public override string ToString()
        {
            string text = "provider error";
            if (HttpStatusCode.HasValue) text += " HTTP " + HttpStatusCode.Value;
            if (!String.IsNullOrWhiteSpace(ErrorType)) text += " " + ErrorType;
            if (!String.IsNullOrWhiteSpace(Message)) text += ": " + Message;
            return text;
        }

        #endregion
    }
}
