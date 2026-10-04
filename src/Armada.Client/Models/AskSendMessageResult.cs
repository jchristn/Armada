namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Result of sending an Ask Armada message (the turn runs in the background).
    /// </summary>
    public class AskSendMessageResult
    {
        #region Public-Members

        /// <summary>
        /// Persisted user message id.
        /// </summary>
        public string MessageId { get; set; } = "";

        /// <summary>
        /// Captain turn id.
        /// </summary>
        public string TurnId { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskSendMessageResult()
        {
        }

        #endregion
    }
}
