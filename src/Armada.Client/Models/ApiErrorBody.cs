namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Error body returned by the Armada API for a non-success response.
    /// </summary>
    public class ApiErrorBody
    {
        #region Public-Members

        /// <summary>
        /// Error category, for example BadRequest, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Human-readable message, or null.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Longer description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Top-level machine-readable code, or null.
        /// </summary>
        public string? Code { get; set; } = null;

        /// <summary>
        /// Request id echoed by the server, or null.
        /// </summary>
        public string? RequestId { get; set; } = null;

        /// <summary>
        /// Structured data, or null.
        /// </summary>
        public ApiErrorData? Data { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ApiErrorBody()
        {
        }

        #endregion
    }
}
