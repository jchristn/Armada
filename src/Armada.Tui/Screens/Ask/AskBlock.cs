namespace Armada.Tui.Screens.Ask
{
    using System.Collections.Generic;
    using Armada.Core.Models;
    using TUIKit;

    /// <summary>
    /// One laid-out block of the Ask transcript: its rendered lines at the current width and what it refers to (the
    /// message, the confirm card's proposal, the hosted work card and its rows), so the view can focus it and act on it.
    /// </summary>
    public class AskBlock
    {
        #region Public-Members

        /// <summary>
        /// Stable key (message id, or <c>older</c>, <c>empty</c>, <c>stream</c>, <c>waiting</c>, <c>error</c>).
        /// </summary>
        public string Key { get; set; } = "";

        /// <summary>
        /// Kind.
        /// </summary>
        public AskBlockKindEnum Kind { get; set; } = AskBlockKindEnum.Message;

        /// <summary>
        /// Message, or null.
        /// </summary>
        public AskMessage? Message { get; set; } = null;

        /// <summary>
        /// Proposal shown as a confirm card in this block, or null.
        /// </summary>
        public AskActionProposal? Proposal { get; set; } = null;

        /// <summary>
        /// CLI permission request shown as a card in this block, or null.
        /// </summary>
        public CliPermissionRequest? CliRequest { get; set; } = null;

        /// <summary>
        /// Tracked work whose live card this block hosts, or null.
        /// </summary>
        public string? WorkId { get; set; } = null;

        /// <summary>
        /// Tracked work a milestone refers to whose card lives elsewhere ("show live card"), or null.
        /// </summary>
        public string? LinkedWorkId { get; set; } = null;

        /// <summary>
        /// Rendered lines (without the selection gutter). Never null.
        /// </summary>
        public List<StyledText> Lines { get; set; } = new List<StyledText>();

        /// <summary>
        /// Line index within <see cref="Lines"/> where each work card row starts. Never null.
        /// </summary>
        public List<int> RowLines { get; set; } = new List<int>();

        /// <summary>
        /// Number of lines of each work card row. Never null.
        /// </summary>
        public List<int> RowHeights { get; set; } = new List<int>();

        /// <summary>
        /// Mission id of each work card row (target rows carry their mission id, or null). Never null.
        /// </summary>
        public List<string?> RowMissionIds { get; set; } = new List<string?>();

        /// <summary>
        /// Pull request URL of each work card row, or null. Never null.
        /// </summary>
        public List<string?> RowPrUrls { get; set; } = new List<string?>();

        /// <summary>
        /// Route opened by Enter on each work card row. Never null.
        /// </summary>
        public List<string?> RowRoutes { get; set; } = new List<string?>();

        /// <summary>
        /// Clickable confirm card buttons, with <see cref="AskCardButton.Line"/> relative to <see cref="Lines"/>. Never
        /// null.
        /// </summary>
        public List<AskCardButton> Buttons { get; set; } = new List<AskCardButton>();

        /// <summary>
        /// The block can take focus.
        /// </summary>
        public bool Focusable { get; set; } = true;

        /// <summary>
        /// First line of the block in the whole transcript (set by the view).
        /// </summary>
        public int Top { get; set; } = 0;

        /// <summary>
        /// Markdown copied by <c>y</c>.
        /// </summary>
        public string CopyText { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskBlock()
        {
        }

        #endregion
    }
}
