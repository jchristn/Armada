namespace Armada.Tui.Approvals
{
    using TUIKit;

    /// <summary>
    /// A clickable decision button drawn on an Approvals center row (<c>[Approve]</c>, <c>[Reject]</c>, <c>[Deny]</c>,
    /// ...): where it was drawn and which decision key it stands for, so a click runs the same confirmation and call as
    /// the key.
    /// </summary>
    public class ApprovalButton
    {
        #region Public-Members

        /// <summary>
        /// Where the button was drawn, in screen coordinates.
        /// </summary>
        public Rect Area { get; set; } = Rect.Empty;

        /// <summary>
        /// Key of the approval item the button belongs to.
        /// </summary>
        public string ItemKey { get; set; } = "";

        /// <summary>
        /// The decision key it stands for (for example <c>a</c> approve, <c>r</c> reject, <c>d</c> deny).
        /// </summary>
        public char DecisionKey { get; set; } = 'a';

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ApprovalButton()
        {
        }

        #endregion
    }
}
