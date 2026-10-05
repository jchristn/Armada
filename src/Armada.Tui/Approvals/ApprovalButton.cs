namespace Armada.Tui.Approvals
{
    /// <summary>
    /// What a clickable decision button on an Approvals center row (<c>[Approve] a</c>, <c>[Reject] r</c>,
    /// <c>[Deny] d</c>, ...) does: the item it belongs to and the decision key it stands for, so a click runs the same
    /// confirmation and call as the key. It is the action of a TUIKit <c>ClickRegion</c>, which holds where the button
    /// was drawn.
    /// </summary>
    public class ApprovalButton
    {
        #region Public-Members

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
