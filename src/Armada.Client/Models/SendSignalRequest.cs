namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Request to send a signal to a captain or broadcast to the Admiral.
    /// </summary>
    public class SendSignalRequest
    {
        #region Public-Members

        /// <summary>
        /// Recipient captain id, or null for an Admiral broadcast.
        /// </summary>
        public string? ToCaptainId { get; set; } = null;

        /// <summary>
        /// Signal type.
        /// </summary>
        public string Type { get; set; } = "";

        /// <summary>
        /// Signal payload, or null.
        /// </summary>
        public string? Payload { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SendSignalRequest()
        {
        }

        #endregion
    }
}
