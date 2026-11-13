namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// A page of thread messages in ascending sequence order.
    /// </summary>
    public class AskMessagePage
    {
        #region Public-Members

        /// <summary>
        /// Messages, oldest first, each with ToolCalls, Proposal, and TrackedWork populated.
        /// </summary>
        [CollectionBound(200, Reason = "AskMessageEnumerateRequest.PageSize is clamped to 1..200; send_ask_message recent messages hold at most 10.")]
        public List<AskMessage> Messages
        {
            get => _Messages;
            set => _Messages = value ?? new List<AskMessage>();
        }

        /// <summary>
        /// Whether older messages exist before the first message of this page.
        /// </summary>
        public bool HasMore { get; set; } = false;

        #endregion

        #region Private-Members

        private List<AskMessage> _Messages = new List<AskMessage>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskMessagePage()
        {
        }

        #endregion
    }
}
