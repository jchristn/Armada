namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Request to send an Ask Armada message.
    /// </summary>
    public class AskSendMessageRequest
    {
        #region Public-Members

        /// <summary>
        /// Message content.
        /// </summary>
        public string Content { get; set; } = "";

        /// <summary>
        /// Stream thinking for this turn.
        /// </summary>
        public bool ShowThinking { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskSendMessageRequest()
        {
        }

        #endregion
    }
}
