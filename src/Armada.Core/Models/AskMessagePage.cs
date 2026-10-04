namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// A page of thread messages in ascending sequence order.
    /// </summary>
    public class AskMessagePage
    {
        #region Public-Members

        /// <summary>
        /// Messages, oldest first, each with ToolCalls, Proposal, and TrackedWork populated.
        /// </summary>
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
