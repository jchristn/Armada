namespace Armada.Tui.Screens.Operations
{
    using Armada.Core.Models;

    /// <summary>
    /// What one <see cref="BlackBoxFailurePanel"/> load read: the entry and its user and agent briefs.
    /// </summary>
    public class BlackBoxPanelLoad
    {
        #region Public-Members

        /// <summary>
        /// Entry, or null.
        /// </summary>
        public BlackBoxEntry? Entry { get; }

        /// <summary>
        /// User audience brief, or null.
        /// </summary>
        public string? UserBrief { get; }

        /// <summary>
        /// Agent audience (FIX THIS) brief, or null.
        /// </summary>
        public string? AgentBrief { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="userBrief">User brief.</param>
        /// <param name="agentBrief">Agent brief.</param>
        public BlackBoxPanelLoad(BlackBoxEntry? entry, string? userBrief, string? agentBrief)
        {
            Entry = entry;
            UserBrief = userBrief;
            AgentBrief = agentBrief;
        }

        #endregion
    }
}
