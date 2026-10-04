namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/ask/threads/{id}/messages.
    /// </summary>
    public class AskMessageSendRequest
    {
        #region Public-Members

        /// <summary>
        /// The user's message. Required; maximum 32000 characters.
        /// </summary>
        public string Content
        {
            get => _Content;
            set => _Content = value ?? String.Empty;
        }

        /// <summary>
        /// Ask the captain to surface its reasoning for this turn. Default false.
        /// </summary>
        public bool? ShowThinking { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Content = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskMessageSendRequest()
        {
        }

        #endregion
    }
}
