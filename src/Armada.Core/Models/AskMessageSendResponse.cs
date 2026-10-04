namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Response of POST /api/v1/ask/threads/{id}/messages (202).
    /// </summary>
    public class AskMessageSendResponse
    {
        #region Public-Members

        /// <summary>
        /// Persisted user message (amg_ prefix).
        /// </summary>
        public string MessageId
        {
            get => _MessageId;
            set => _MessageId = value ?? String.Empty;
        }

        /// <summary>
        /// Identifier of the captain turn running in the background (stamped on ask.* events), or null when the thread has no captain.
        /// </summary>
        public string? TurnId { get; set; } = null;

        #endregion

        #region Private-Members

        private string _MessageId = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskMessageSendResponse()
        {
        }

        #endregion
    }
}
