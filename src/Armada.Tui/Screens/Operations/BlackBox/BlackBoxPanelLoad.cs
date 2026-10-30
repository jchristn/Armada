namespace Armada.Tui.Screens.Operations
{
    using Armada.Core.Models;

    /// <summary>
    /// What one <see cref="BlackBoxFailurePanel"/> load read: the entry and its FIX THIS brief.
    /// </summary>
    public class BlackBoxPanelLoad
    {
        #region Public-Members

        /// <summary>
        /// Entry, or null.
        /// </summary>
        public BlackBoxEntry? Entry { get; }

        /// <summary>
        /// FIX THIS brief, or null.
        /// </summary>
        public string? Brief { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="brief">Brief.</param>
        public BlackBoxPanelLoad(BlackBoxEntry? entry, string? brief)
        {
            Entry = entry;
            Brief = brief;
        }

        #endregion
    }
}
