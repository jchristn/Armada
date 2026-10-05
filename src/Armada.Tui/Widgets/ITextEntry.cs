namespace Armada.Tui.Widgets
{
    /// <summary>
    /// Marks a widget that takes typed text while it has focus (a text box, a text area, the Ask composer, the dock's
    /// input, the workspace editor). Printable keys go into it, so single-key shortcuts of the surrounding screen do not
    /// work until focus leaves it; the status bar uses this to show how to leave instead of shortcuts that would only
    /// type a letter (and <c>F1</c> for help instead of <c>?</c>).
    /// </summary>
    public interface ITextEntry
    {
        /// <summary>
        /// True while the widget takes typed text (false while it is read-only).
        /// </summary>
        bool AcceptsText { get; }
    }
}
