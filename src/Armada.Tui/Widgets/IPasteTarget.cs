namespace Armada.Tui.Widgets
{
    /// <summary>
    /// A widget that accepts bracketed paste. The shell forwards TUIKit's paste events to the focused widget.
    /// </summary>
    public interface IPasteTarget
    {
        /// <summary>
        /// Insert pasted text.
        /// </summary>
        /// <param name="text">Pasted text.</param>
        /// <returns>True when consumed.</returns>
        bool HandlePaste(string text);
    }
}
