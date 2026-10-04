namespace Armada.Client.Socket
{
    using System;

    /// <summary>
    /// Payload of ask.proposal: a proposal was created or changed.
    /// </summary>
    public class AskProposalEvent
    {
        #region Public-Members

        /// <summary>
        /// Thread id.
        /// </summary>
        public string? ThreadId { get; set; } = null;

        /// <summary>
        /// The proposal.
        /// </summary>
        public Armada.Core.Models.AskActionProposal? Proposal { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskProposalEvent()
        {
        }

        #endregion
    }
}
