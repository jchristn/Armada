namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Why a captain's model or runtime configuration failed validation: a machine-readable reason plus the
    /// human-readable message. REST returns it as the Data of the 400 error; MCP returns the reason as the error Code.
    /// </summary>
    public class CaptainModelValidationFailure
    {
        #region Public-Members

        /// <summary>
        /// Machine-readable reason.
        /// </summary>
        public CaptainModelValidationFailureEnum Reason { get; set; } = CaptainModelValidationFailureEnum.ModelRejected;

        /// <summary>
        /// Human-readable message (may include runtime output).
        /// </summary>
        public string Message { get; set; } = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Build a failure.
        /// </summary>
        /// <param name="reason">Machine-readable reason.</param>
        /// <param name="message">Human-readable message.</param>
        /// <returns>The failure.</returns>
        public static CaptainModelValidationFailure Create(CaptainModelValidationFailureEnum reason, string message)
        {
            CaptainModelValidationFailure failure = new CaptainModelValidationFailure();
            failure.Reason = reason;
            failure.Message = message ?? String.Empty;
            return failure;
        }

        #endregion
    }
}
