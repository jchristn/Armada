namespace Armada.Tui.Widgets
{
    /// <summary>
    /// A widget that knows the keys for its current focus state (a filter row, a form, the Ask dock). The shell asks the
    /// widgets on the focus path, deepest first, and the first answer becomes the status bar hints for the focused
    /// control (see <see cref="FocusHints"/>); the enclosing screen then adds what still works there.
    /// </summary>
    public interface IFocusHintSource
    {
        /// <summary>
        /// Hints for the current focus state inside this widget.
        /// </summary>
        /// <returns>Hints, or null when the widget has none of its own (the next widget up the focus path is asked).</returns>
        FocusHints? GetFocusHints();
    }
}
