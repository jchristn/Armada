namespace Armada.Tui.Screens.Ask
{
    /// <summary>
    /// A clickable button drawn on an Ask confirm card (<c>[Approve]</c>, <c>[Reject]</c>, <c>[Arguments]</c>): where
    /// it sits in its block's lines and what it does, so a mouse press works whatever has keyboard focus.
    /// </summary>
    public class AskCardButton
    {
        #region Public-Members

        /// <summary>
        /// Line index within the block's (or card's) lines.
        /// </summary>
        public int Line { get; set; } = 0;

        /// <summary>
        /// First cell of the button within the line (before the transcript's selection gutter).
        /// </summary>
        public int X { get; set; } = 0;

        /// <summary>
        /// Width of the button in cells.
        /// </summary>
        public int Width { get; set; } = 0;

        /// <summary>
        /// What the button does.
        /// </summary>
        public AskCardActionEnum Action { get; set; } = AskCardActionEnum.Approve;

        /// <summary>
        /// Proposal the button acts on.
        /// </summary>
        public string ProposalId { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskCardButton()
        {
        }

        #endregion
    }
}
