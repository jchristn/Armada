namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Structured data carried by an Armada API error response (for example vessel import errors carry a code and path).
    /// </summary>
    public class ApiErrorData
    {
        #region Public-Members

        /// <summary>
        /// Machine-readable error code, or null.
        /// </summary>
        public string? Code { get; set; } = null;

        /// <summary>
        /// Path the error refers to, or null.
        /// </summary>
        public string? Path { get; set; } = null;

        /// <summary>
        /// Detail message, or null.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Request id, when the server echoes one, or null.
        /// </summary>
        public string? RequestId { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ApiErrorData()
        {
        }

        #endregion
    }
}
